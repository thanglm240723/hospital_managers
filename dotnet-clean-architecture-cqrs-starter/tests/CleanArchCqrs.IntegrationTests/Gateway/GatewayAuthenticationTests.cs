using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Gateway;

[Collection(IntegrationCollection.Name)]
public class GatewayAuthenticationTests : IAsyncLifetime
{
    private const string Me = "/api/v1/auth/me";
    private readonly ContainersFixture _containers;
    private ApiFactory _api = default!;
    private GatewayFactory _gateway = default!;

    public GatewayAuthenticationTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _api = await ApiFactory.CreateAsync(_containers);
        _gateway = new GatewayFactory(_api, _containers.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _api.DisposeAsync();
    }

    private IDatabase Redis => _api.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private int BackendCalls(string path) => _gateway.BackendRequests.Count(r => r.Path == path);

    private async Task<(string Email, AuthTestClient Client)> LoggedInViaGatewayAsync(GatewayFactory? gateway = null)
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_api, email);
        var client = new AuthTestClient((gateway ?? _gateway).CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (email, client);
    }

    private static Guid FamilyOf(string accessToken)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim("fid").Value);

    private static string SignedToken(string signingKey, DateTimeOffset issuedAt, TimeSpan lifetime)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestConstants.JwtIssuer,
            Audience = TestConstants.JwtAudience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = (issuedAt + lifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["fid"] = Guid.NewGuid().ToString(),
                ["sv"] = 1
            }
        });

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task LoginIsPublic_ProtectedRouteWorksWithToken()
    {
        var (_, client) = await LoggedInViaGatewayAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task NoToken_Is401AtGateway_BackendNeverCalled()
    {
        var response = await new AuthTestClient(_gateway.CreateHttpsClient()).GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(response));
        Assert.Equal(0, BackendCalls(Me));
    }

    [Fact]
    public async Task ForgedSignature_Is401AtGateway()
    {
        var client = new AuthTestClient(_gateway.CreateHttpsClient())
        {
            AccessToken = SignedToken("attacker-key-0123456789-0123456789-0123456789", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15))
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
        Assert.Equal(0, BackendCalls(Me));
    }

    [Fact]
    public async Task ExpiredToken_Is401AtGateway()
    {
        var client = new AuthTestClient(_gateway.CreateHttpsClient())
        {
            AccessToken = SignedToken(TestConstants.JwtSigningKey, DateTimeOffset.UtcNow.AddMinutes(-20), TimeSpan.FromMinutes(15))
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task AfterLogout_OldAccessTokenIsRejectedImmediately()
    {
        var (_, client) = await LoggedInViaGatewayAsync();
        var oldAccessToken = client.AccessToken;
        (await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false)).EnsureSuccessStatusCode();

        client.AccessToken = oldAccessToken;
        var viaGateway = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, viaGateway.StatusCode);
        // API một mình chỉ kiểm chữ ký — chính Gateway là lớp thu hồi phiên ngay lập tức.
        var direct = new AuthTestClient(_api.CreateHttpsClient()) { AccessToken = oldAccessToken };
        Assert.Equal(HttpStatusCode.OK, (await direct.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task RedisMiss_GatewayAsksIdentityServiceAndRecaches()
    {
        var (_, client) = await LoggedInViaGatewayAsync();
        var key = CacheKeys.Session(FamilyOf(client.AccessToken!));
        await Redis.KeyDeleteAsync(key);

        var response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(BackendCalls("/internal/sessions/validate") >= 1);
        Assert.True(await Redis.KeyExistsAsync(key));
    }

    [Fact]
    public async Task AfterPasswordChange_TokenWithOldSecurityVersionIsRejected()
    {
        var (_, client) = await LoggedInViaGatewayAsync();
        var oldAccessToken = client.AccessToken;

        (await client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = TestData.DefaultPassword, newPassword = "Brand-New-Pass-99" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);   // token mới
        client.AccessToken = oldAccessToken;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task DeactivatedAccount_OldTokenIsRejectedImmediately()
    {
        var (email, client) = await LoggedInViaGatewayAsync();
        var admin = await _api.LoginAsAdminAsync();
        var userId = await TestData.QueryAsync(_api, db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        (await admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/deactivate")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task ClientSuppliedInternalHeaders_AreStripped()
    {
        var (_, client) = await LoggedInViaGatewayAsync();

        await client.SendAsync(HttpMethod.Get, Me, csrf: false,
            extraHeaders: new Dictionary<string, string> { ["X-Internal-Key"] = "forged", ["X-Internal-User"] = "admin" });

        var forwarded = _gateway.BackendRequests.Last(r => r.Path == Me);
        Assert.DoesNotContain(forwarded.HeaderNames, h => h.StartsWith("X-Internal-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RedisMissAndIdentityServiceDown_Is503()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString, identityServiceDown: true);
        var (_, client) = await LoggedInViaGatewayAsync(gateway);
        await Redis.KeyDeleteAsync(CacheKeys.Session(FamilyOf(client.AccessToken!)));

        var response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("dependency_unavailable", await CodeAsync(response));
    }
}

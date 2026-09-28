using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class MeAndSessionsTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public MeAndSessionsTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<AuthTestClient> LoginAsync(string email)
    {
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401ProblemDetails()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient()).GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_ReturnsRolesAndEffectivePermissionsWithoutPasswordHash()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email, roleCodes: [SystemRoles.Admin]);
        var client = await LoginAsync(email);

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(raw);
        Assert.Equal(email, body.RootElement.GetProperty("email").GetString());
        Assert.Equal("admin", body.RootElement.GetProperty("roles")[0].GetProperty("code").GetString());
        Assert.Contains(body.RootElement.GetProperty("permissions").EnumerateArray(), p => p.GetString() == Permissions.Users.Read);
    }

    [Fact]
    public async Task Sessions_ListsActiveSessionsAndMarksCurrent()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var first = await LoginAsync(email);
        await LoginAsync(email);

        var sessions = await (await first.GetAsync("/api/v1/auth/sessions")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, sessions.GetArrayLength());
        var currentFid = new JsonWebTokenHandler().ReadJsonWebToken(first.AccessToken).GetClaim("fid").Value;
        var current = Assert.Single(sessions.EnumerateArray(), s => s.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(currentFid, current.GetProperty("id").GetString());
    }

    [Fact]
    public async Task RevokeSession_OwnOtherSession_KillsItsRefresh()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var laptop = await LoginAsync(email);
        var phone = await LoginAsync(email);
        var phoneFid = new JsonWebTokenHandler().ReadJsonWebToken(phone.AccessToken).GetClaim("fid").Value;

        var response = await laptop.SendAsync(HttpMethod.Post, $"/api/v1/auth/sessions/{phoneFid}/revoke");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.RefreshAsync()).StatusCode);
    }

    /// M4 (final review): thu hồi đúng phiên đang dùng phải xoá cookie __Host-rt/__Host-csrf giống logout-all,
    /// nếu không FE còn giữ cookie vô dụng và có thể gửi lại, kích hoạt phát hiện tái sử dụng (reuse).
    [Fact]
    public async Task RevokeSession_OwnCurrentSession_ClearsCookies()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = await LoginAsync(email);
        var currentFid = new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim("fid").Value;

        var response = await client.SendAsync(HttpMethod.Post, $"/api/v1/auth/sessions/{currentFid}/revoke");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(client.RefreshToken);
        Assert.Null(client.CsrfToken);
    }

    [Fact]
    public async Task RevokeSession_SomeoneElsesSession_Returns404()
    {
        var mine = TestData.NewEmail();
        var theirs = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, mine);
        await TestData.CreateUserAsync(_factory, theirs);
        var me = await LoginAsync(mine);
        var other = await LoginAsync(theirs);
        var otherFid = new JsonWebTokenHandler().ReadJsonWebToken(other.AccessToken).GetClaim("fid").Value;

        var response = await me.SendAsync(HttpMethod.Post, $"/api/v1/auth/sessions/{otherFid}/revoke");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task LogoutAll_KillsEverySession()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var first = await LoginAsync(email);
        var second = await LoginAsync(email);

        var response = await first.SendAsync(HttpMethod.Post, "/api/v1/auth/logout-all");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.RefreshAsync()).StatusCode);
    }
}

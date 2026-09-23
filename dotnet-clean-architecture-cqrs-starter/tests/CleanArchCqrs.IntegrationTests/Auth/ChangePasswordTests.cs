using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class ChangePasswordTests : IAsyncLifetime
{
    private const string NewPassword = "Brand-New-Pass-99";
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public ChangePasswordTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(string Email, AuthTestClient Client)> LoggedInAsync()
    {
        var email = TestData.NewEmail("staff");
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (email, client);
    }

    private static Task<HttpResponseMessage> ChangeAsync(AuthTestClient client, string current, string next, bool csrf = true)
        => client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = current, newPassword = next }, csrf: csrf);

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

    [Fact]
    public async Task WrongCurrentPassword_Returns400OnCurrentPassword()
    {
        var (_, client) = await LoggedInAsync();

        var response = await ChangeAsync(client, "Not-My-Password-1", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("currentPassword", out _));
    }

    [Theory]
    [InlineData("short")]
    [InlineData(TestData.DefaultPassword)]
    public async Task InvalidNewPassword_Returns400OnNewPassword(string next)
    {
        var (_, client) = await LoggedInAsync();

        var response = await ChangeAsync(client, TestData.DefaultPassword, next);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("newPassword", out _));
    }

    [Fact]
    public async Task NewPasswordContainingEmailName_Returns400()
    {
        var (email, client) = await LoggedInAsync();
        var local = email[..email.IndexOf('@')];

        var response = await ChangeAsync(client, TestData.DefaultPassword, $"{local}-X1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingCsrf_Returns403()
    {
        var (_, client) = await LoggedInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ChangeAsync(client, TestData.DefaultPassword, NewPassword, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task Success_BumpsSecurityVersion_KeepsCurrentSession_KillsOthers()
    {
        var (email, current) = await LoggedInAsync();
        var other = new AuthTestClient(_factory.CreateHttpsClient());
        (await other.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        var oldSv = int.Parse(new JsonWebTokenHandler().ReadJsonWebToken(current.AccessToken).GetClaim("sv").Value);

        var response = await ChangeAsync(current, TestData.DefaultPassword, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(current.AccessToken);
        Assert.Equal(oldSv + 1, int.Parse(jwt.GetClaim("sv").Value));
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.RefreshAsync()).StatusCode);
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(Guid.Parse(jwt.GetClaim("fid").Value))));
    }

    [Fact]
    public async Task SeededAdmin_MustChangeThenCanLogInWithNewPassword()
    {
        var admin = await _factory.LoginAsAdminAsync();

        var me = await (await admin.GetAsync("/api/v1/auth/me")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
    }
}

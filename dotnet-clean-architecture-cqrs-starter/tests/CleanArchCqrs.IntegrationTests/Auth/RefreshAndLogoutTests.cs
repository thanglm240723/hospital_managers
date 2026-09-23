using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class RefreshAndLogoutTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private ApiFactory _factory = default!;

    public RefreshAndLogoutTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
        => _factory = await ApiFactory.CreateAsync(_containers, configureServices: s => s.AddSingleton<TimeProvider>(_time));

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<AuthTestClient> LoggedInAsync()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static Guid FamilyOf(AuthTestClient client)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim("fid").Value);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Refresh_RotatesCookieAndKeepsSameSession()
    {
        var client = await LoggedInAsync();
        var oldRefresh = client.RefreshToken;
        var family = FamilyOf(client);

        var response = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(oldRefresh, client.RefreshToken);
        Assert.Equal(family, FamilyOf(client));
    }

    [Fact]
    public async Task Refresh_ReplayedOldToken_RevokesWholeFamily()
    {
        var client = await LoggedInAsync();
        var family = FamilyOf(client);
        var stolen = client.RefreshToken;
        (await client.RefreshAsync()).EnsureSuccessStatusCode();
        var legit = client.RefreshToken;

        client.RefreshToken = stolen;
        var replay = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Null(client.RefreshToken);   // cookie đã bị xoá
        client.RefreshToken = legit;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.RefreshReuse && a.ResourceId == family.ToString())));
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(family)));
    }

    [Fact]
    public async Task Refresh_TwoConcurrentWithSameToken_ExactlyOneSucceeds_FamilyRevoked()
    {
        var first = await LoggedInAsync();
        var second = first.CloneWith(_factory.CreateHttpsClient());

        var responses = await Task.WhenAll(first.RefreshAsync(), second.RefreshAsync());

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        var winner = responses[0].StatusCode == HttpStatusCode.OK ? first : second;
        Assert.Equal(HttpStatusCode.Unauthorized, (await winner.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Refresh_MissingCsrfHeader_Returns403()
    {
        var client = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf: false, bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
    }

    [Fact]
    public async Task Refresh_ForeignOrigin_Returns403()
    {
        var client = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", bearer: false, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient()).RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterSevenDays_Returns401EvenIfActive()
    {
        var client = await LoggedInAsync();
        _time.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesSessionClearsCookiesAndCache()
    {
        var client = await LoggedInAsync();
        var family = FamilyOf(client);
        var refresh = client.RefreshToken;

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(client.RefreshToken);
        Assert.Null(client.CsrfToken);
        client.RefreshToken = refresh;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(family)));
    }

    [Fact]
    public async Task Logout_WithoutCookie_IsStill204()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient())
            .SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}

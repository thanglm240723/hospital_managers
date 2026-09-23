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
public class InternalSessionValidationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public InternalSessionValidationTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private IDatabase Redis => _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private async Task<(AuthTestClient Client, Guid Fid, int Sv)> LoggedInAsync()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken);
        return (client, Guid.Parse(jwt.GetClaim("fid").Value), int.Parse(jwt.GetClaim("sv").Value));
    }

    private async Task<HttpResponseMessage> ValidateAsync(Guid fid, int sv, string? key = TestConstants.InternalApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/sessions/validate")
        {
            Content = JsonContent.Create(new { familyId = fid, sv })
        };
        if (key is not null) request.Headers.Add("X-Internal-Key", key);
        return await _factory.CreateHttpsClient().SendAsync(request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task MissingOrWrongKey_Returns401(string? key)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ValidateAsync(Guid.NewGuid(), 1, key)).StatusCode);

    [Fact]
    public async Task ValidSession_ReturnsValidAndReloadsCache()
    {
        var (_, fid, sv) = await LoggedInAsync();
        await Redis.KeyDeleteAsync(CacheKeys.Session(fid));

        var body = await (await ValidateAsync(fid, sv)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.True(body.GetProperty("absExp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.True(await Redis.KeyExistsAsync(CacheKeys.Session(fid)));
    }

    [Fact]
    public async Task StaleSecurityVersion_IsInvalid()
    {
        var (_, fid, sv) = await LoggedInAsync();

        var body = await (await ValidateAsync(fid, sv - 1)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task LoggedOutSession_IsInvalidAndNotRecached()
    {
        var (client, fid, sv) = await LoggedInAsync();
        await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        var body = await (await ValidateAsync(fid, sv)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.False(await Redis.KeyExistsAsync(CacheKeys.Session(fid)));
    }
}

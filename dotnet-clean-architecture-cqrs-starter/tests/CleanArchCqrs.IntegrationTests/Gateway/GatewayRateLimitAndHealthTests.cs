using System.Net;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Gateway;

[Collection(IntegrationCollection.Name)]
public class GatewayRateLimitAndHealthTests : IAsyncLifetime
{
    private const string Login = "/api/v1/auth/login";
    private readonly ContainersFixture _containers;
    private ApiFactory _api = default!;

    public GatewayRateLimitAndHealthTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _api = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task EleventhLoginFromSameIpWithinAMinute_Is429AtGateway()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString);
        var client = new AuthTestClient(gateway.CreateHttpsClient());

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(TestData.NewEmail(), "Wrong-Password-1")).StatusCode);
        var eleventh = await client.LoginAsync(TestData.NewEmail(), "Wrong-Password-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.True(int.Parse(eleventh.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Equal(10, gateway.BackendRequests.Count(r => r.Path == Login));
    }

    [Fact]
    public async Task RedisDown_RateLimitIsSkippedAndHealthIsDegraded()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var client = new AuthTestClient(gateway.CreateHttpsClient());

        for (var i = 0; i < 11; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(TestData.NewEmail(), "Wrong-Password-1")).StatusCode);

        var health = await gateway.CreateHttpsClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Degraded", await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_IsAnonymousAndHealthy()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString);

        var health = await gateway.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
    }
}

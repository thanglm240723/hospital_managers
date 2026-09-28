using System.Net;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Gateway;

[Collection(IntegrationCollection.Name)]
public class GatewayRateLimitAndHealthTests : IAsyncLifetime
{
    private const string Login = "/api/v1/auth/login";
    private const string Refresh = "/api/v1/auth/refresh";
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

    [Fact(Skip = "chờ slice refresh-logout")]
    public async Task ThirtyFirstRefreshFromSameIpWithinAMinute_Is429AtGateway()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString);
        var client = new AuthTestClient(gateway.CreateHttpsClient());

        for (var i = 0; i < 30; i++)
            Assert.NotEqual(HttpStatusCode.TooManyRequests, (await client.RefreshAsync()).StatusCode);
        var thirtyFirst = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.TooManyRequests, thirtyFirst.StatusCode);
        Assert.True(int.Parse(thirtyFirst.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Equal(30, gateway.BackendRequests.Count(r => r.Path == Refresh));
    }

    [Fact]
    public async Task SpoofedXForwardedFor_DoesNotEvadeIpRateLimit()
    {
        // ForwardedHeaders:KnownProxies không khai TestServer's caller ⇒ Gateway phải bỏ qua X-Forwarded-For
        // do client tự khai; nếu không, đổi header mỗi request sẽ né được rate limit theo IP.
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString);
        var client = new AuthTestClient(gateway.CreateHttpsClient());

        for (var i = 0; i < 10; i++)
        {
            var spoofedIp = $"203.0.113.{i}";
            var response = await client.SendAsync(HttpMethod.Post, Login, new { email = TestData.NewEmail(), password = "Wrong-Password-1" },
                csrf: false, bearer: false, extraHeaders: new Dictionary<string, string> { ["X-Forwarded-For"] = spoofedIp });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        var eleventh = await client.SendAsync(HttpMethod.Post, Login, new { email = TestData.NewEmail(), password = "Wrong-Password-1" },
            csrf: false, bearer: false, extraHeaders: new Dictionary<string, string> { ["X-Forwarded-For"] = "203.0.113.250" });

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.Equal(10, gateway.BackendRequests.Count(r => r.Path == Login));
    }

    [Fact(Skip = "chờ slice health DB")]
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

    [Fact(Skip = "chờ slice health DB")]
    public async Task Health_IsAnonymousAndHealthy()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString);

        var health = await gateway.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
    }
}

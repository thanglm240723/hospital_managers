using System.Net;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Health;

[Collection(IntegrationCollection.Name)]
public class HealthEndpointTests
{
    private readonly ContainersFixture _containers;

    public HealthEndpointTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task Health_AllDependenciesUp_ReturnsHealthy()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);

        var response = await factory.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_RedisDown_ReturnsDegradedButStillOk()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });

        var response = await factory.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync());
    }
}

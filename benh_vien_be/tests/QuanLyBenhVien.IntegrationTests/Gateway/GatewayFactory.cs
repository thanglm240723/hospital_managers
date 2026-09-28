using System.Collections.Concurrent;
using System.Net;
using QuanLyBenhVien.Gateway.Auth;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace QuanLyBenhVien.IntegrationTests.Gateway;

public sealed class GatewayFactory : WebApplicationFactory<QuanLyBenhVien.Gateway.Program>
{
    private readonly ApiFactory _api;
    private readonly bool _identityServiceDown;
    private readonly Dictionary<string, string?> _settings;

    public ConcurrentQueue<RecordedRequest> BackendRequests { get; } = new();
    public IPAddress ClientIp { get; } = new([10, (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(1, 255)]);

    public GatewayFactory(ApiFactory api, string redisConnectionString, IDictionary<string, string?>? overrides = null,
        bool identityServiceDown = false)
    {
        _api = api;
        _identityServiceDown = identityServiceDown;
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Jwt:Issuer"] = TestConstants.JwtIssuer,
            ["Jwt:Audience"] = TestConstants.JwtAudience,
            ["Jwt:SigningKey"] = TestConstants.JwtSigningKey,
            ["Identity:InternalBaseUrl"] = "http://api/",
            ["Identity:InternalApiKey"] = TestConstants.InternalApiKey,
            ["ReverseProxy:Clusters:identity-cluster:Destinations:primary:Address"] = "http://api/",
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                _settings[key] = value;
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IForwarderHttpClientFactory>(
                new TestForwarderHttpClientFactory(() => new RecordingHandler(_api.Server.CreateHandler(), BackendRequests)));
            services.AddHttpClient(SessionValidator.HttpClientName).ConfigurePrimaryHttpMessageHandler(() =>
                _identityServiceDown
                    ? new UnreachableHandler()
                    : new RecordingHandler(_api.Server.CreateHandler(), BackendRequests));
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new FixedRemoteIpStartupFilter(ClientIp));
        });
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Identity service unreachable (test)");
    }
}

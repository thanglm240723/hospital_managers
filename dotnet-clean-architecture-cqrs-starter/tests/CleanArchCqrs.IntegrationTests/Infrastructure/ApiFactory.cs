using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<CleanArchCqrs.API.Program>
{
    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public string AdminEmail { get; }
    public string DatabaseConnectionString { get; }

    private ApiFactory(string databaseConnectionString, string redisConnectionString,
        IDictionary<string, string?>? overrides, Action<IServiceCollection>? configureServices)
    {
        DatabaseConnectionString = databaseConnectionString;
        AdminEmail = $"admin-{Guid.NewGuid():N}@test.local";
        _configureServices = configureServices;
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = databaseConnectionString,
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Jwt:Issuer"] = TestConstants.JwtIssuer,
            ["Jwt:Audience"] = TestConstants.JwtAudience,
            ["Jwt:SigningKey"] = TestConstants.JwtSigningKey,
            ["Auth:CsrfKey"] = TestConstants.CsrfKey,
            ["Auth:InternalApiKey"] = TestConstants.InternalApiKey,
            ["Auth:AllowedOrigins:0"] = TestConstants.Origin,
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = TestConstants.AdminTempPassword,
            ["Seed:AdminFullName"] = "Test Admin",
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                _settings[key] = value;
    }

    public static async Task<ApiFactory> CreateAsync(ContainersFixture containers,
        IDictionary<string, string?>? overrides = null, Action<IServiceCollection>? configureServices = null)
        => new(await containers.CreateDatabaseAsync(), containers.RedisConnectionString, overrides, configureServices);

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
        if (_configureServices is not null)
            builder.ConfigureTestServices(_configureServices);
    }
}

using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Infrastructure.Identity;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.Infrastructure.HealthChecks;
using QuanLyBenhVien.Infrastructure.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure;

/// <summary>
/// Đăng ký adapter bảo mật và Redis của layer Infrastructure.
/// </summary>
public static class DependencyInjection
{
    /// Trùng CsrfTokenService.MinKeyLength — khoá HMAC dưới ngưỡng này coi như không cấu hình.
    private const int MinKeyLength = 32;

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart: một SigningKey rỗng/ngắn trước đây chỉ lộ ra ở request đầu tiên (500). Thất bại
        // ngay khi host khởi động thì an toàn hơn nhiều so với để lọt ra production rồi mới phát hiện.
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection("Jwt"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && o.SigningKey.Length >= MinKeyLength,
                $"Jwt:SigningKey must be at least {MinKeyLength} characters.")
            .ValidateOnStart();

        // Mục rỗng trong AllowedOrigins (vd. cấu hình rỗng "") sẽ khớp Origin rỗng của một request KHÔNG
        // gửi header Origin — cho request đó lọt qua kiểm tra CSRF. Lọc bỏ ngay khi bind (PostConfigure chạy
        // trước Validate), trước khi bất kỳ filter nào đọc options.
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection("Auth"))
            .Validate(o => !string.IsNullOrWhiteSpace(o.CsrfKey) && o.CsrfKey.Length >= MinKeyLength,
                $"Auth:CsrfKey must be at least {MinKeyLength} characters.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.InternalApiKey), "Auth:InternalApiKey must not be empty.")
            .ValidateOnStart();
        services.PostConfigure<AuthOptions>(o =>
            o.AllowedOrigins = o.AllowedOrigins.Where(origin => !string.IsNullOrWhiteSpace(origin)).ToArray());

        services.TryAddSingleton(TimeProvider.System);

        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "localhost:6379");
            options.AbortOnConnectFail = false;   // Redis sập lúc khởi động không được làm sập app
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 1000;
            options.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded);

        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<IAccessTokenIssuer, JwtTokenService>();
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<ICsrfTokenService, CsrfTokenService>();
        services.AddSingleton<IRequestOriginPolicy, RequestOriginPolicy>();
        services.AddSingleton<ISessionCache, SessionCache>();
        services.AddSingleton<ICacheKeyEvictor, RedisCacheKeyEvictor>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddHostedService<CacheInvalidationWorker>();

        return services;
    }
}

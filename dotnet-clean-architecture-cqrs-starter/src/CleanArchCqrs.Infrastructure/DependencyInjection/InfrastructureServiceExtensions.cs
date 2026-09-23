using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Auditing;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.Infrastructure.HealthChecks;
using CleanArchCqrs.Infrastructure.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Persistence.Interceptors;
using CleanArchCqrs.Infrastructure.Persistence.Seed;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Infrastructure layer services.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// Trùng CsrfTokenService.MinKeyLength — khoá HMAC dưới ngưỡng này coi như không cấu hình.
    private const int MinKeyLength = 32;

    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IAuditRecorder, AuditRecorder>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IRoleRepository, RoleRepository>();
        services.AddScoped<ISessionRepository, SessionRepository>();
        services.AddScoped<IIdentityReadService, IdentityReadService>();
        services.AddScoped<ISessionValidationService, SessionValidationService>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
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
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<ICsrfTokenService, CsrfTokenService>();

        services.TryAddSingleton(TimeProvider.System);
        services.Configure<SeedOptions>(configuration.GetSection("Seed"));
        services.AddScoped<IdentitySeeder>();

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
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded);

        services.AddSingleton<ISessionCache, SessionCache>();
        services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
        services.AddScoped<ICacheInvalidator, CacheInvalidator>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<CacheInvalidationProcessor>();
        services.AddHostedService<CacheInvalidationWorker>();

        return services;
    }
}

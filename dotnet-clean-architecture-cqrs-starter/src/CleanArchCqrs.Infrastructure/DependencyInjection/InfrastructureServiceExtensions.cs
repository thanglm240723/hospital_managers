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
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Infrastructure layer services.
/// </summary>
public static class InfrastructureServiceExtensions
{
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
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        services.Configure<AuthOptions>(configuration.GetSection("Auth"));
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
        services.AddScoped<CacheInvalidationProcessor>();
        services.AddHostedService<CacheInvalidationWorker>();

        return services;
    }
}

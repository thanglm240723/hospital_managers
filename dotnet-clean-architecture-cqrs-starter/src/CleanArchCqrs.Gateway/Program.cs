using System.Net;
using CleanArchCqrs.Gateway.Auth;
using CleanArchCqrs.Gateway.DependencyInjection;
using CleanArchCqrs.Gateway.HealthChecks;
using CleanArchCqrs.Gateway.Middleware;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

namespace CleanArchCqrs.Gateway;

/// <summary>
/// API Gateway — điểm vào công khai duy nhất. Xác thực JWT + phiên (Redis → API nội bộ) rồi mới định tuyến.
/// Phân quyền và nghiệp vụ nằm ở backend.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        builder.Services.AddGatewayReverseProxy(builder.Configuration);
        builder.Services.AddGatewayAuthentication(builder.Configuration);

        builder.Services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded);

        // Chỉ tin X-Forwarded-For từ load balancer đã khai báo — không thì client tự khai IP để né rate limit.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });

        var app = builder.Build();

        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<StripInternalHeadersMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseMiddleware<IpRateLimitMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapReverseProxy();

        app.Run();
    }
}

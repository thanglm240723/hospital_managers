using CleanArchCqrs.Gateway.Auth;
using CleanArchCqrs.Gateway.DependencyInjection;
using CleanArchCqrs.Gateway.Middleware;
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

        var app = builder.Build();

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<StripInternalHeadersMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapReverseProxy();

        app.Run();
    }
}

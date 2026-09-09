using CleanArchCqrs.Gateway.DependencyInjection;

namespace CleanArchCqrs.Gateway;

/// <summary>
/// API Gateway startup - the single public entry point in front of the backend services.
/// This layer only routes: authentication and authorization are handled by the backend
/// services themselves. Business logic never belongs here.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Routes and clusters are declarative - see the "ReverseProxy" section in appsettings.json.
        builder.Services.AddGatewayReverseProxy(builder.Configuration);

        var app = builder.Build();

        app.MapReverseProxy();

        app.Run();
    }
}

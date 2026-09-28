namespace QuanLyBenhVien.Gateway.DependencyInjection;

/// <summary>
/// Extension methods for registering Gateway layer services.
/// </summary>
public static class GatewayServiceExtensions
{
    /// <summary>
    /// Registers YARP with the routes and clusters declared in the "ReverseProxy" configuration section.
    /// The Authorization header is forwarded unchanged; the API re-validates the JWT signature as defence in depth.
    /// </summary>
    public static IServiceCollection AddGatewayReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));

        return services;
    }
}

namespace CleanArchCqrs.Gateway.DependencyInjection;

/// <summary>
/// Extension methods for registering Gateway layer services.
/// </summary>
public static class GatewayServiceExtensions
{
    /// <summary>
    /// Registers YARP with the routes and clusters declared in the "ReverseProxy" configuration section.
    /// Incoming headers - including Authorization - are forwarded to the destination as-is,
    /// so each backend service validates the token itself.
    /// </summary>
    public static IServiceCollection AddGatewayReverseProxy(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddReverseProxy()
            .LoadFromConfig(configuration.GetSection("ReverseProxy"));

        return services;
    }
}

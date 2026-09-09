using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.Infrastructure.DependencyInjection;

/// <summary>
/// Extension methods for registering Infrastructure layer services.
/// </summary>
public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, string? connectionString = null)
    {
        return services;
    }
}

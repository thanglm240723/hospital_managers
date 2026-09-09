using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.Application.DependencyInjection;

/// <summary>
/// Extension methods for registering Application layer services.
/// </summary>
public static class ApplicationServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // MediatR - register all handlers in this assembly
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceExtensions).Assembly));

        return services;
    }
}

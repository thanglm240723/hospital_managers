using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using CleanArchCqrs.Infrastructure.Persistence;
namespace CleanArchCqrs.Infrastructure.DependencyInjection;


public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
            //opt.AddInterceptors(sp.GetRequiredService<DomainEventDispatchInterceptor>());
        });

        return services;
    }
}

using System.Reflection;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Application.Behaviors;
using QuanLyBenhVien.Application.Common.Caching;

namespace QuanLyBenhVien.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = Assembly.GetExecutingAssembly();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddOpenBehavior(typeof(LoggingBehavior<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        services.AddScoped<CacheInvalidationProcessor>();
        services.AddScoped<ICacheInvalidator, CacheInvalidator>();

        return services;
    }
}

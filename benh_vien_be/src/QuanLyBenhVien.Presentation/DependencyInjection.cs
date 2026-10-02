using Carter;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Presentation.Security;
using QuanLyBenhVien.Presentation.Http;

namespace QuanLyBenhVien.Presentation;

public static class DependencyInjection
{
    public static IServiceCollection AddPresentation(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddCarter();
        services.AddScoped<AuthCookieWriter>();

        // Cùng section "Auth" với QuanLyBenhVien.Infrastructure.Security.AuthOptions (đã validate ở AddInfrastructure).
        services.AddOptions<InternalApiOptions>()
            .Configure(o => o.Key = configuration["Auth:InternalApiKey"] ?? "");

        return services;
    }
}

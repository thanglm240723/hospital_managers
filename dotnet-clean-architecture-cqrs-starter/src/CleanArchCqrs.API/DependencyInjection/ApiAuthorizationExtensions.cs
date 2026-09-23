using CleanArchCqrs.API.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.DependencyInjection;

public static class ApiAuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            var signedIn = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PasswordChangeRequirement())
                .Build();
            options.DefaultPolicy = signedIn;
            options.FallbackPolicy = signedIn;   // từ chối mặc định: quên gắn attribute vẫn phải đăng nhập
        });
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, PasswordChangeRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        return services;
    }
}

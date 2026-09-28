using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace QuanLyBenhVien.Presentation.Endpoints.V1.Auth;

public sealed class AuthEndpoints : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/auth")
        .WithTags("Authentication");
 
        group.MapPost("/login", LoginAsync)
        .AllowAnonymous()
        .WithName("LoginV1");
        group.MapPost("/refresh", RefreshAsync)
        .AllowAnonymous()
        .WithName("RefreshV1");
        group.MapPost("/logout", LogoutAsync)
        .AllowAnonymous()
        .WithName("LogoutV1");
        group.MapGet("/me", MeAsync)
        .RequireAuthorization()
        .WithName("MeV1");
        group.MapPost("/change-password", ChangePasswordAsync)
        .RequireAuthorization()
        .WithName("ChangePasswordV1");
        group.MapPost("/logout-all", LogoutAllAsync).RequireAuthorization().WithName("LogoutAllV1");
       
    }

    private static Task<IResult> LoginAsync(LoginRequest req, HttpContext http, ISender sender, CancellationToken ct) =>
        throw new NotImplementedException();

    private static Task<IResult> RefreshAsync(HttpContext http, ISender sender, CancellationToken ct) =>
        throw new NotImplementedException();

    private static Task<IResult> LogoutAsync(HttpContext http, ISender sender, CancellationToken ct) =>
        throw new NotImplementedException();

    private static Task<IResult> MeAsync(ISender sender, CancellationToken ct) =>
        throw new NotImplementedException();

    private static Task<IResult> ChangePasswordAsync(ChangePasswordRequest req, HttpContext http, ISender sender, CancellationToken ct) =>
        throw new NotImplementedException();

    private static Task<IResult> LogoutAllAsync(HttpContext http, ISender sender, CancellationToken ct) =>
        throw new NotImplementedException();
}

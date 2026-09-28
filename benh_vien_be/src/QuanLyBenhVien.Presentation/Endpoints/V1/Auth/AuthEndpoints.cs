using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.GetMe;
using QuanLyBenhVien.Application.Features.Auth.Login;
using QuanLyBenhVien.Presentation.Auth;
using QuanLyBenhVien.Presentation.Http;

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

    private static async Task<IResult> LoginAsync(LoginRequest req, HttpContext http, ISender sender, AuthCookieWriter cookies, CancellationToken ct)
    {
        var result = await sender.Send(new LoginCommand(req.Email, req.Password), ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem(http);
        }

        var tokens = result.Value;
        cookies.Write(http.Response, tokens.RefreshToken, tokens.CsrfToken, tokens.SessionExpiresAtUtc);
        return Results.Ok(new AccessTokenDto(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.MustChangePassword));
    }

    private static Task<IResult> RefreshAsync(HttpContext http, ISender sender, CancellationToken ct) =>
        Task.FromResult(NotImplemented(http));

    private static Task<IResult> LogoutAsync(HttpContext http, ISender sender, CancellationToken ct) =>
        Task.FromResult(NotImplemented(http));

    private static async Task<IResult> MeAsync(HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetMeQuery(), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static Task<IResult> ChangePasswordAsync(ChangePasswordRequest req, HttpContext http, ISender sender, CancellationToken ct) =>
        Task.FromResult(NotImplemented(http));

    private static Task<IResult> LogoutAllAsync(HttpContext http, ISender sender, CancellationToken ct) =>
        Task.FromResult(NotImplemented(http));

    private static IResult NotImplemented(HttpContext http) =>
        ProblemResponses.Create(http, StatusCodes.Status501NotImplemented, "not_implemented",
            "Chức năng chưa được hỗ trợ.");
}

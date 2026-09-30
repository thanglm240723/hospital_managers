using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Features.Auth.ChangePassword;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.GetMe;
using QuanLyBenhVien.Application.Features.Auth.Login;
using QuanLyBenhVien.Application.Features.Auth.Logout;
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
        .RequireRefreshCookieCsrf()
        .AllowWhilePasswordChangeRequired()
        .WithName("LogoutV1");
        group.MapGet("/me", MeAsync)
        .RequireAuthorization()
        .AllowWhilePasswordChangeRequired()
        .WithName("MeV1");
        group.MapPost("/change-password", ChangePasswordAsync)
        .RequireAuthorization()
        .RequireCsrf()
        .AllowWhilePasswordChangeRequired()
        .WithName("ChangePasswordV1");
        group.MapPost("/logout-all", LogoutAllAsync)
        .RequireAuthorization()
        .RequireCsrf()
        .AllowWhilePasswordChangeRequired()
        .WithName("LogoutAllV1");
       
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

    private static async Task<IResult> LogoutAsync(HttpContext http, ISender sender, IRefreshTokenGenerator tokenGenerator,
        IRefreshSessionLookup sessionLookup, AuthCookieWriter cookies, CancellationToken ct)
    {
        // Cookie thiếu/không nhận diện được family: không có gì để thu hồi — 204, xoá cookie, không mutate (Task 1).
        // Cookie nhận diện được: đã qua filter CSRF (RequireRefreshCookieCsrf), thu hồi thật thuộc Task 2.
        var cookie = http.Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (string.IsNullOrEmpty(cookie) || await sessionLookup.FindAsync(tokenGenerator.Hash(cookie), ct) is null)
        {
            cookies.Clear(http.Response);
            return Results.NoContent();
        }

        var result = await sender.Send(new LogoutCommand(cookie), ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem(http);
        }

        cookies.Clear(http.Response);
        return Results.NoContent();
    }

    private static async Task<IResult> MeAsync(HttpContext http, ISender sender, CancellationToken ct)
    {
        var result = await sender.Send(new GetMeQuery(), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static async Task<IResult> ChangePasswordAsync(ChangePasswordRequest req, HttpContext http, ISender sender, CancellationToken ct)
    {
        // Thành công chỉ trả AccessTokenDto; giữ nguyên refresh cookie/family hiện tại.
        var result = await sender.Send(new ChangePasswordCommand(req.CurrentPassword, req.NewPassword), ct);
        return result.IsFailure ? result.Error!.ToProblem(http) : Results.Ok(result.Value);
    }

    private static Task<IResult> LogoutAllAsync(HttpContext http, ISender sender, CancellationToken ct) =>
        Task.FromResult(NotImplemented(http));

    private static IResult NotImplemented(HttpContext http) =>
        ProblemResponses.Create(http, StatusCodes.Status501NotImplemented, "not_implemented",
            "Chức năng chưa được hỗ trợ.");
}

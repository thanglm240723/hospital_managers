using Carter;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.ChangePassword;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.GetMe;
using QuanLyBenhVien.Application.Features.Auth.Login;
using QuanLyBenhVien.Application.Features.Auth.Logout;
using QuanLyBenhVien.Application.Features.Auth.LogoutAll;
using QuanLyBenhVien.Application.Features.Auth.RefreshSession;
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
        .RequireRefreshCookieCsrf()
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

    private static async Task<IResult> RefreshAsync(HttpContext http, ISender sender, AuthCookieWriter cookies, CancellationToken ct)
    {
        // Đã qua filter CSRF (RequireRefreshCookieCsrf) khi cookie nhận diện được family; thiếu/rác ⇒ handler trả 401.
        var cookie = http.Request.Cookies[AuthCookieWriter.RefreshCookie] ?? string.Empty;
        var result = await sender.Send(new RefreshSessionCommand(cookie), ct);
        if (result.IsFailure)
        {
            // 401 nghiệp vụ: xoá cookie để client không gửi lại token chết. Lỗi hạ tầng là exception ⇒ 5xx, không tới đây.
            if (result.Error!.Type == ErrorType.Unauthorized)
            {
                cookies.Clear(http.Response);
            }

            return result.Error.ToProblem(http);
        }

        var tokens = result.Value;
        cookies.Write(http.Response, tokens.RefreshToken, tokens.CsrfToken, tokens.SessionExpiresAtUtc);
        return Results.Ok(new AccessTokenDto(tokens.AccessToken, tokens.AccessTokenExpiresAtUtc, tokens.MustChangePassword));
    }

    private static async Task<IResult> LogoutAsync(HttpContext http, ISender sender, AuthCookieWriter cookies, CancellationToken ct)
    {
        // Đã qua filter CSRF (RequireRefreshCookieCsrf) khi cookie nhận diện được family.
        // Cookie thiếu/không nhận diện được: handler trả thành công mà không mutate — vẫn 204 + xoá cookie.
        var cookie = http.Request.Cookies[AuthCookieWriter.RefreshCookie];
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

    private static async Task<IResult> LogoutAllAsync(HttpContext http, ISender sender, AuthCookieWriter cookies, CancellationToken ct)
    {
        var result = await sender.Send(new LogoutAllCommand(), ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem(http);
        }

        cookies.Clear(http.Response);
        return Results.NoContent();
    }
}

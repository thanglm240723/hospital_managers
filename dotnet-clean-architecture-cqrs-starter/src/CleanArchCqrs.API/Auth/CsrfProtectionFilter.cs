using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Auth;

/// Đặc tả kỹ thuật §4.1: kiểm Origin theo allowlist + CSRF token có chữ ký gắn phiên.
/// SameSite=Strict trên cookie chỉ chặn request KHÁC SITE; request CÙNG SITE nhưng khác origin (vd. một
/// subdomain khác) vẫn mang cookie đi kèm — đó chính là kịch bản header CSRF phải chặn, không phải chỉ
/// phòng hờ. Vì vậy việc bắt buộc header không được phép tuỳ vào cookie phía client còn giữ hay không;
/// nó phải dựa trên trạng thái phiên phía server (family Active hay không).
public sealed class CsrfProtectionFilter : IAsyncActionFilter
{
    public const string HeaderName = "X-CSRF-Token";

    private readonly ICsrfTokenService _csrf;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ISessionRepository _sessions;
    private readonly AuthOptions _options;

    public CsrfProtectionFilter(ICsrfTokenService csrf, IRefreshTokenGenerator refreshTokens,
        ISessionRepository sessions, IOptions<AuthOptions> options)
    {
        _csrf = csrf;
        _refreshTokens = refreshTokens;
        _sessions = sessions;
        _options = options.Value;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var origin = http.Request.Headers.Origin.ToString();
        if (!_options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = Reject(http);
            return;
        }

        var familyId = await ResolveActiveFamilyIdAsync(http);
        if (familyId is not null && !_csrf.IsValid(familyId.Value, http.Request.Headers[HeaderName].ToString()))
        {
            context.Result = Reject(http);
            return;
        }

        await next();
    }

    /// Có access token ⇒ family trong claim fid (JWT hợp lệ luôn ứng với phiên Active tại lúc phát hành).
    /// Không có ⇒ tra family qua cookie __Host-rt, nhưng CHỈ coi là "xác định được family" khi family đó
    /// đang Active — family đã Revoked/không tồn tại (token cũ, đã dùng, gõ mò...) không còn phiên nào để
    /// CSRF bảo vệ, nhường cho handler tự quyết (401 cho refresh, 204 cho logout).
    private async Task<Guid?> ResolveActiveFamilyIdAsync(HttpContext http)
    {
        if (Guid.TryParse(http.User.FindFirst("fid")?.Value, out var fid)) return fid;
        var refreshToken = http.Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (string.IsNullOrEmpty(refreshToken)) return null;

        var resolved = await _sessions.FindFamilyStatusByTokenHashAsync(_refreshTokens.Hash(refreshToken), http.RequestAborted);
        return resolved is { Status: SessionStatus.Active } ? resolved.Value.FamilyId : null;
    }

    private static Microsoft.AspNetCore.Mvc.ObjectResult Reject(HttpContext http)
        => ProblemResponseWriter.ToResult(http, StatusCodes.Status403Forbidden, ErrorCodes.CsrfFailed,
            "Yêu cầu không hợp lệ (CSRF).");
}

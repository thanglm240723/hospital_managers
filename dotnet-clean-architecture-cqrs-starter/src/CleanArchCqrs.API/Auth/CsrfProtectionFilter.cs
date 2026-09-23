using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Auth;

/// Đặc tả kỹ thuật §4.1: kiểm Origin theo allowlist + CSRF token có chữ ký gắn phiên (cộng thêm SameSite=Strict của cookie).
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

        var familyId = await ResolveFamilyIdAsync(http);
        // Double-submit chỉ có ý nghĩa khi trình duyệt còn giữ cookie __Host-csrf. Nếu cookie này
        // đã bị xoá (logout/reuse ở tab khác) mà cookie __Host-rt còn sót lại (stale), request vẫn
        // cùng-origin (SameSite=Strict đã chặn mọi request khác-origin gửi kèm cookie) — không có gì
        // để đối chiếu, nên bỏ qua và để handler tự quyết (thường là 401 vì phiên đã vô hiệu).
        var hasCsrfCookie = http.Request.Cookies.ContainsKey(AuthCookieWriter.CsrfCookie);
        if (familyId is not null && hasCsrfCookie
            && !_csrf.IsValid(familyId.Value, http.Request.Headers[HeaderName].ToString()))
        {
            context.Result = Reject(http);
            return;
        }

        await next();
    }

    /// Có access token ⇒ family trong claim fid. Không có ⇒ family của refresh cookie.
    /// Không xác định được family ⇒ request không thể đổi trạng thái phiên nào, để handler xử lý (401/204).
    private async Task<Guid?> ResolveFamilyIdAsync(HttpContext http)
    {
        if (Guid.TryParse(http.User.FindFirst("fid")?.Value, out var fid)) return fid;
        var refreshToken = http.Request.Cookies[AuthCookieWriter.RefreshCookie];
        return string.IsNullOrEmpty(refreshToken)
            ? null
            : await _sessions.FindFamilyIdByTokenHashAsync(_refreshTokens.Hash(refreshToken), http.RequestAborted);
    }

    private static Microsoft.AspNetCore.Mvc.ObjectResult Reject(HttpContext http)
        => ProblemResponseWriter.ToResult(http, StatusCodes.Status403Forbidden, ErrorCodes.CsrfFailed,
            "Yêu cầu không hợp lệ (CSRF).");
}

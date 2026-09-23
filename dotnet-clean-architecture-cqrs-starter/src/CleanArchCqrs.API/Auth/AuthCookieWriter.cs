using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.API.Auth;

/// Đặc tả kỹ thuật §4.1: host-only, Secure, SameSite=Strict, Path=/, không Domain (điều kiện của tiền tố __Host-).
public sealed class AuthCookieWriter
{
    public const string RefreshCookie = "__Host-rt";
    public const string CsrfCookie = "__Host-csrf";

    private readonly ICsrfTokenService _csrf;
    private readonly TimeProvider _time;

    public AuthCookieWriter(ICsrfTokenService csrf, TimeProvider time)
    {
        _csrf = csrf;
        _time = time;
    }

    public void Write(HttpResponse response, string refreshToken, Guid sessionFamilyId, DateTimeOffset sessionExpiresAtUtc)
    {
        var maxAge = sessionExpiresAtUtc - _time.GetUtcNow();
        response.Cookies.Append(RefreshCookie, refreshToken, Options(httpOnly: true, maxAge));
        // Không HttpOnly: JS đọc để gửi lại trong header X-CSRF-Token.
        response.Cookies.Append(CsrfCookie, _csrf.Create(sessionFamilyId), Options(httpOnly: false, maxAge));
    }

    public void Clear(HttpResponse response)
    {
        response.Cookies.Delete(RefreshCookie, Options(httpOnly: true, maxAge: null));
        response.Cookies.Delete(CsrfCookie, Options(httpOnly: false, maxAge: null));
    }

    private static CookieOptions Options(bool httpOnly, TimeSpan? maxAge) => new()
    {
        HttpOnly = httpOnly,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };
}

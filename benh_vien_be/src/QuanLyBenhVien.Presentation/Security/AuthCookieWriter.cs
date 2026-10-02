using Microsoft.AspNetCore.Http;

namespace QuanLyBenhVien.Presentation.Security;

/// Đặc tả kỹ thuật §4.1: host-only, Secure, SameSite=Strict, Path=/, không Domain (điều kiện của tiền tố __Host-).
public sealed class AuthCookieWriter(TimeProvider time)
{
    public const string RefreshCookie = "__Host-rt";
    public const string CsrfCookie = "__Host-csrf";

    public void Write(HttpResponse response, string refreshToken, string csrfToken, DateTimeOffset sessionExpiresAtUtc)
    {
        var remaining = sessionExpiresAtUtc - time.GetUtcNow();
        var maxAge = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        response.Cookies.Append(RefreshCookie, refreshToken, Options(httpOnly: true, maxAge));
        // Không HttpOnly: JS đọc để gửi lại trong header X-CSRF-Token.
        response.Cookies.Append(CsrfCookie, csrfToken, Options(httpOnly: false, maxAge));
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

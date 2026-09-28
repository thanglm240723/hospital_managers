using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Time.Testing;
using QuanLyBenhVien.Presentation.Auth;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Presentation.Auth;

public class AuthCookieWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_SetsRefreshCookie_HttpOnlySecureStrictHostOnly()
    {
        var context = new DefaultHttpContext();
        var writer = new AuthCookieWriter(new FakeTimeProvider(Now));

        writer.Write(context.Response, "refresh-token", "csrf-token", Now.AddDays(7));

        var setCookies = context.Response.Headers.SetCookie.ToArray();
        var refreshCookie = Assert.Single(setCookies, c => c!.StartsWith(AuthCookieWriter.RefreshCookie + "=", StringComparison.Ordinal));
        Assert.Contains("httponly", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("path=/", refreshCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", refreshCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_SetsCsrfCookie_NotHttpOnly()
    {
        var context = new DefaultHttpContext();
        var writer = new AuthCookieWriter(new FakeTimeProvider(Now));

        writer.Write(context.Response, "refresh-token", "csrf-token", Now.AddDays(7));

        var setCookies = context.Response.Headers.SetCookie.ToArray();
        var csrfCookie = Assert.Single(setCookies, c => c!.StartsWith(AuthCookieWriter.CsrfCookie + "=", StringComparison.Ordinal));
        Assert.DoesNotContain("httponly", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("secure", csrfCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", csrfCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Write_MaxAge_MatchesRemainingTimeUntilSessionExpiry()
    {
        var context = new DefaultHttpContext();
        var writer = new AuthCookieWriter(new FakeTimeProvider(Now));

        writer.Write(context.Response, "refresh-token", "csrf-token", Now.AddDays(1));

        var setCookies = context.Response.Headers.SetCookie.ToArray();
        var refreshCookie = Assert.Single(setCookies, c => c!.StartsWith(AuthCookieWriter.RefreshCookie + "=", StringComparison.Ordinal));
        Assert.Contains("max-age=86400", refreshCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Clear_DeletesBothCookies_WithMatchingAttributes()
    {
        var context = new DefaultHttpContext();
        var writer = new AuthCookieWriter(new FakeTimeProvider(Now));

        writer.Clear(context.Response);

        var setCookies = context.Response.Headers.SetCookie.ToArray();
        Assert.Contains(setCookies, c => c!.StartsWith(AuthCookieWriter.RefreshCookie + "=", StringComparison.Ordinal));
        Assert.Contains(setCookies, c => c!.StartsWith(AuthCookieWriter.CsrfCookie + "=", StringComparison.Ordinal));
        // Xóa cookie: expires trong quá khứ (Kestrel/CookieOptions dùng Expires khi Delete).
        Assert.Contains(setCookies, c => c!.Contains("expires=", StringComparison.OrdinalIgnoreCase));
    }
}

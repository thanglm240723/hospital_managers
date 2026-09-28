using Microsoft.AspNetCore.Http;
using QuanLyBenhVien.Application.Common.Identity;

namespace QuanLyBenhVien.API.Security;

public sealed class HttpRequestContext(IHttpContextAccessor accessor) : IRequestContext
{
    private const int MaxUserAgentLength = 512;

    public string? CorrelationId => NullIfEmpty(accessor.HttpContext?.Request.Headers["X-Correlation-Id"].ToString());

    /// Sau UseForwardedHeaders đây là IP client thật do Gateway chuyển xuống.
    public string? IpAddress => accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = NullIfEmpty(accessor.HttpContext?.Request.Headers.UserAgent.ToString());
            return value is { Length: > MaxUserAgentLength } ? value[..MaxUserAgentLength] : value;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

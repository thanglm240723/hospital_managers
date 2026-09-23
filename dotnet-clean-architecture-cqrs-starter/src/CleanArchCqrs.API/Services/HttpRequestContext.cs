using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.API.Services;

public sealed class HttpRequestContext : IRequestContext
{
    private const int MaxUserAgentLength = 512;
    private readonly IHttpContextAccessor _accessor;

    public HttpRequestContext(IHttpContextAccessor accessor) => _accessor = accessor;

    public string? CorrelationId => NullIfEmpty(_accessor.HttpContext?.Request.Headers["X-Correlation-Id"].ToString());

    /// Sau UseForwardedHeaders đây là IP client thật do Gateway chuyển xuống.
    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = NullIfEmpty(_accessor.HttpContext?.Request.Headers.UserAgent.ToString());
            return value is { Length: > MaxUserAgentLength } ? value[..MaxUserAgentLength] : value;
        }
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

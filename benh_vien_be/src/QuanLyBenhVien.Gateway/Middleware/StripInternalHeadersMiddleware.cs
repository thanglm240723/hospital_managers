namespace QuanLyBenhVien.Gateway.Middleware;

/// Client không bao giờ được tự gửi header nội bộ (X-Internal-Key, X-Internal-User…) xuống backend.
public sealed class StripInternalHeadersMiddleware
{
    private const string Prefix = "X-Internal-";
    private readonly RequestDelegate _next;

    public StripInternalHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        foreach (var name in context.Request.Headers.Keys
                     .Where(k => k.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            context.Request.Headers.Remove(name);
        return _next(context);
    }
}

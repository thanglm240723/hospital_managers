using System.Text.Json;

namespace QuanLyBenhVien.Gateway.Errors;

/// Bản sao tối giản của writer bên API — cùng hình dạng RFC 9457 { type, title, status, code, traceId }.
public static class ProblemResponseWriter
{
    public static Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].ToString();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
            ["traceId"] = string.IsNullOrEmpty(correlationId) ? context.TraceIdentifier : correlationId,
        }));
    }
}

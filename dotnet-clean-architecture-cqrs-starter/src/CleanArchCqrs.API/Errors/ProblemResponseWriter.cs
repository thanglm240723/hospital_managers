using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Errors;

/// Một nơi duy nhất dựng body Problem Details (RFC 9457) cho mọi lỗi của API.
public static class ProblemResponseWriter
{
    private const string ContentType = "application/problem+json";

    public static Dictionary<string, object?> BuildBody(HttpContext context, int status, string code, string title,
        IDictionary<string, string[]>? errors = null)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var body = new Dictionary<string, object?>
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
            ["traceId"] = string.IsNullOrEmpty(correlationId) ? context.TraceIdentifier : correlationId,
        };
        if (errors is not null)
            body["errors"] = errors.ToDictionary(e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value);
        return body;
    }

    public static async Task WriteAsync(HttpContext context, int status, string code, string title,
        IDictionary<string, string[]>? errors = null, CancellationToken ct = default)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(BuildBody(context, status, code, title, errors)), ct);
    }

    public static ObjectResult ToResult(HttpContext context, int status, string code, string title,
        IDictionary<string, string[]>? errors = null)
        => new(BuildBody(context, status, code, title, errors)) { StatusCode = status, ContentTypes = { ContentType } };
}

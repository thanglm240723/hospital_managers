using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace QuanLyBenhVien.Presentation.Http;

/// Một nơi duy nhất dựng body Problem Details (RFC 9457) cho mọi lỗi của API — dùng chung giữa Presentation
/// (map Result → HTTP) và exception handler của API.
public static class ProblemResponses
{
    private const string ContentType = "application/problem+json";

    public static IDictionary<string, object?> BuildBody(HttpContext http, int status, string code, string title,
        IDictionary<string, string[]>? errors = null)
    {
        var correlationId = http.Request.Headers["X-Correlation-Id"].ToString();
        var body = new Dictionary<string, object?>
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
            ["traceId"] = string.IsNullOrEmpty(correlationId) ? http.TraceIdentifier : correlationId,
        };
        if (errors is not null)
            body["errors"] = errors.ToDictionary(e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value);
        return body;
    }

    public static IResult Create(HttpContext http, int status, string code, string title,
        IDictionary<string, string[]>? errors = null)
        => Results.Json(BuildBody(http, status, code, title, errors), statusCode: status, contentType: ContentType);
}

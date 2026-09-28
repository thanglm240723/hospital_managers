using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace QuanLyBenhVien.API.Middleware;

public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    private const string HeaderName = "X-Correlation-Id";
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && IsValid(existing.ToString())
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Request.Headers[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        context.TraceIdentifier = correlationId;

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    private static bool IsValid(string value) =>
        value.Length is > 0 and <= MaxLength && AllowedCharacters().IsMatch(value);

    [GeneratedRegex("^[A-Za-z0-9._-]+$")]
    private static partial Regex AllowedCharacters();
}

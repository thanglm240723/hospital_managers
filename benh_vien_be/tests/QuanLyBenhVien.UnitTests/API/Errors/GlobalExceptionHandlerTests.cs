using System.Text.Json;
using QuanLyBenhVien.API.Errors;
using QuanLyBenhVien.Application.Common.Exceptions;
using QuanLyBenhVien.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace QuanLyBenhVien.UnitTests.API.Errors;

public class GlobalExceptionHandlerTests
{
    private static async Task<(HttpContext Context, JsonElement Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "corr-123";
        context.Response.Body = new MemoryStream();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, document.RootElement.Clone());
    }

    [Fact]
    public async Task Validation_Returns400WithCamelCaseErrors()
    {
        var (context, body) = await HandleAsync(new ValidationException("CurrentPassword", "Sai"));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.Equal("corr-123", body.GetProperty("traceId").GetString());
        Assert.Equal("Sai", body.GetProperty("errors").GetProperty("currentPassword")[0].GetString());
    }

    [Fact]
    public async Task Unauthorized_Returns401WithMessageAsTitle()
    {
        var (context, body) = await HandleAsync(new UnauthorizedException("Email hoặc mật khẩu không đúng."));

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("unauthenticated", body.GetProperty("code").GetString());
        Assert.Equal("Email hoặc mật khẩu không đúng.", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Forbidden_UsesExceptionCode()
    {
        var (context, body) = await HandleAsync(new ForbiddenException("csrf_failed", "CSRF"));

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("csrf_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task NotFound_Returns404WithGenericTitle()
    {
        var (context, body) = await HandleAsync(new NotFoundException("User with id 'x' was not found."));

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("not_found", body.GetProperty("code").GetString());
        Assert.DoesNotContain("x", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Conflict_UsesExceptionCode()
    {
        var (context, body) = await HandleAsync(new ConflictException("last_admin", "Còn 1 admin"));

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("last_admin", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TooManyRequests_SetsRetryAfterSeconds()
    {
        var (context, body) = await HandleAsync(new TooManyRequestsException(TimeSpan.FromSeconds(89.2), "Chậm lại"));

        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("90", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_Returns500WithoutLeakingMessage()
    {
        var (context, body) = await HandleAsync(new InvalidOperationException("SELECT * FROM secret"));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.DoesNotContain("SELECT", body.ToString());
    }
}

using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using QuanLyBenhVien.API.Middleware;
using QuanLyBenhVien.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace QuanLyBenhVien.UnitTests.API.Middleware;

public class GlobalExceptionHandlerTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static async Task<(HttpContext Context, JsonElement Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext { RequestServices = Services, TraceIdentifier = "corr-123" };
        context.Response.Body = new MemoryStream();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, document.RootElement.Clone());
    }

    [Fact]
    public async Task ValidationException_Returns400WithCamelCaseErrors()
    {
        var (context, body) = await HandleAsync(new ValidationException([new ValidationFailure("CurrentPassword", "Sai")]));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.Equal("corr-123", body.GetProperty("traceId").GetString());
        Assert.Equal("Sai", body.GetProperty("errors").GetProperty("currentPassword")[0].GetString());
    }

    [Fact]
    public async Task BadHttpRequestException_Returns400ValidationFailed()
    {
        var (context, body) = await HandleAsync(new BadHttpRequestException("Failed to read parameter"));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task NotFoundException_Returns404WithMessageAsTitle()
    {
        var (context, body) = await HandleAsync(new NotFoundException("User with id 'x' was not found."));

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("not_found", body.GetProperty("code").GetString());
        Assert.Equal("User with id 'x' was not found.", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task BusinessRuleViolationException_Returns409WithMessageAsTitle()
    {
        var (context, body) = await HandleAsync(new BusinessRuleViolationException("Còn 1 admin"));

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("conflict", body.GetProperty("code").GetString());
        Assert.Equal("Còn 1 admin", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task UnknownException_Returns500WithoutLeakingMessage()
    {
        var (context, body) = await HandleAsync(new InvalidOperationException("SELECT * FROM secret"));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.DoesNotContain("SELECT", body.ToString());
    }
}

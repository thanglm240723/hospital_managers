using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Presentation.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Presentation.Http;

public class ResultExtensionsTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static async Task<(int Status, System.Text.Json.JsonElement Body, string? RetryAfter)> ToProblemAsync(Error error)
    {
        var context = new DefaultHttpContext { RequestServices = Services, TraceIdentifier = "corr-9" };
        context.Response.Body = new MemoryStream();

        await error.ToProblem(context).ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        var retryAfter = context.Response.Headers.RetryAfter.ToString();
        return (context.Response.StatusCode, doc.RootElement.Clone(), retryAfter.Length > 0 ? retryAfter : null);
    }

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Precondition, 412)]
    [InlineData(ErrorType.TooManyRequests, 429)]
    public async Task ErrorType_MapsToExpectedStatus(ErrorType type, int expectedStatus)
    {
        var (status, body, _) = await ToProblemAsync(new Error("some_code", "Thông báo lỗi", type));

        Assert.Equal(expectedStatus, status);
        Assert.Equal("some_code", body.GetProperty("code").GetString());
        Assert.Equal("Thông báo lỗi", body.GetProperty("title").GetString());
        Assert.Equal("corr-9", body.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task RetryAfter_RoundsUpToWholeSecondsMinimumOne()
    {
        var error = new Error("rate_limited", "Chậm lại", ErrorType.TooManyRequests) { RetryAfter = TimeSpan.FromSeconds(90.2) };

        var (_, _, retryAfter) = await ToProblemAsync(error);

        Assert.Equal("91", retryAfter);
    }

    [Fact]
    public async Task RetryAfter_SubSecond_RoundsUpToOne()
    {
        var error = new Error("rate_limited", "Chậm lại", ErrorType.TooManyRequests) { RetryAfter = TimeSpan.FromMilliseconds(200) };

        var (_, _, retryAfter) = await ToProblemAsync(error);

        Assert.Equal("1", retryAfter);
    }
}

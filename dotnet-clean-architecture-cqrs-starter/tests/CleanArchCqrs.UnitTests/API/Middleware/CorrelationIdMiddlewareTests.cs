using CleanArchCqrs.API.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Middleware;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoExistingHeader_GeneratesIdAndSetsRequestAndResponseHeaders()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey("X-Correlation-Id"));
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-Id"));
        Assert.Equal(
            context.Request.Headers["X-Correlation-Id"].ToString(),
            context.Response.Headers["X-Correlation-Id"].ToString());
        Assert.True(Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out _));
    }

    [Fact]
    public async Task InvokeAsync_ExistingHeader_PreservesValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "existing-id-123";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("existing-id-123", context.Response.Headers["X-Correlation-Id"].ToString());
    }
}

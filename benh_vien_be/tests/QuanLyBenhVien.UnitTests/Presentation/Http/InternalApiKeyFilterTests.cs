using QuanLyBenhVien.Presentation.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Presentation.Http;

public class InternalApiKeyFilterTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();
    private const string ValidKey = "internal-key-0123456789";

    private static EndpointFilterInvocationContext Context(string? headerValue)
    {
        var http = new DefaultHttpContext { RequestServices = Services };
        if (headerValue is not null) http.Request.Headers["X-Internal-Key"] = headerValue;
        return new DefaultEndpointFilterInvocationContext(http);
    }

    private static InternalApiKeyFilter CreateFilter() => new(Options.Create(new InternalApiOptions { Key = ValidKey }));

    [Fact]
    public async Task CorrectKey_CallsNext()
    {
        var filter = CreateFilter();
        var called = false;
        EndpointFilterDelegate next = _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        await filter.InvokeAsync(Context(ValidKey), next);

        Assert.True(called);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("wrong-key")]
    [InlineData("internal-key-0123456789-longer")]
    public async Task WrongOrMissingKey_Returns401WithoutCallingNext(string? headerValue)
    {
        var filter = CreateFilter();
        var called = false;
        EndpointFilterDelegate next = _ =>
        {
            called = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };
        var context = Context(headerValue);

        var result = await filter.InvokeAsync(context, next);

        Assert.False(called);
        await ((IResult)result!).ExecuteAsync(context.HttpContext);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.HttpContext.Response.StatusCode);
    }
}

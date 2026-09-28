using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Presentation.Http;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Presentation.Http;

public class ProblemResponsesTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();

    [Fact]
    public async Task Create_NotImplemented_Returns501WithStableCodeAndNormalizedTraceId()
    {
        var context = new DefaultHttpContext { RequestServices = Services, TraceIdentifier = "normalized-id-123" };
        context.Request.Headers["X-Correlation-Id"] = "raw-client-value; drop table";

        var result = ProblemResponses.Create(context, StatusCodes.Status501NotImplemented, "not_implemented",
            "Chức năng chưa được hỗ trợ.");

        await result.ExecuteAsync(context);

        Assert.Equal(StatusCodes.Status501NotImplemented, context.Response.StatusCode);
        var body = ProblemResponses.BuildBody(context, StatusCodes.Status501NotImplemented, "not_implemented",
            "Chức năng chưa được hỗ trợ.");
        Assert.Equal("not_implemented", body["code"]);
        Assert.Equal("normalized-id-123", body["traceId"]);
    }
}

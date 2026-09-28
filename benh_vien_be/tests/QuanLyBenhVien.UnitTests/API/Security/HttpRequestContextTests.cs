using System.Net;
using QuanLyBenhVien.API.Security;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace QuanLyBenhVien.UnitTests.API.Security;

public class HttpRequestContextTests
{
    [Fact]
    public void ReadsCorrelationIpAndTruncatedUserAgent()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-Correlation-Id"] = "corr-1";
        http.Request.Headers.UserAgent = new string('u', 600);
        http.Connection.RemoteIpAddress = IPAddress.Parse("10.1.2.3");

        var context = new HttpRequestContext(new HttpContextAccessor { HttpContext = http });

        Assert.Equal("corr-1", context.CorrelationId);
        Assert.Equal("10.1.2.3", context.IpAddress);
        Assert.Equal(512, context.UserAgent!.Length);
    }

    [Fact]
    public void NoHttpContext_ReturnsNulls()
    {
        var context = new HttpRequestContext(new HttpContextAccessor());

        Assert.Null(context.CorrelationId);
        Assert.Null(context.IpAddress);
        Assert.Null(context.UserAgent);
    }
}

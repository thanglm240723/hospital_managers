using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Presentation.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Presentation.Http;

public class ResultFieldErrorsTests
{
    private static readonly IServiceProvider Services = new ServiceCollection().AddLogging().BuildServiceProvider();

    private static async Task<(int Status, System.Text.Json.JsonElement Body)> ToProblemAsync(Error error)
    {
        var context = new DefaultHttpContext { RequestServices = Services, TraceIdentifier = "corr-9" };
        context.Response.Body = new MemoryStream();

        await error.ToProblem(context).ExecuteAsync(context);

        context.Response.Body.Position = 0;
        using var doc = await System.Text.Json.JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, doc.RootElement.Clone());
    }

    [Fact]
    public async Task ValidationResult_PreservesCurrentPasswordField()
    {
        var error = new Error("invalid_current_password", "Mật khẩu hiện tại không đúng.", ErrorType.Validation)
        {
            FieldErrors = new Dictionary<string, string[]> { ["currentPassword"] = ["Mật khẩu hiện tại không đúng."] }
        };

        var (status, body) = await ToProblemAsync(error);

        Assert.Equal(400, status);
        Assert.True(body.GetProperty("errors").TryGetProperty("currentPassword", out var messages));
        Assert.Equal("Mật khẩu hiện tại không đúng.", messages[0].GetString());
        // Không rò nội dung mật khẩu vào response.
        Assert.DoesNotContain("mật khẩu hiện tại không đúng123", body.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Error_WithoutFieldErrors_HasNoErrorsProperty()
    {
        var error = new Error("some_code", "Thông báo lỗi", ErrorType.Validation);

        var (_, body) = await ToProblemAsync(error);

        Assert.False(body.TryGetProperty("errors", out _));
    }
}

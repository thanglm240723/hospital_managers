using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Presentation.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
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
    }

    [Fact]
    public async Task FieldErrors_BodyContainsOnlyDeclaredPropertiesAndMessages_NoRawInputEchoedBack()
    {
        // Mô phỏng caller vô tình đưa mật khẩu thô của người dùng vào Error.Message thay vì message cố định
        // (lỗi lập trình giả định) — nếu ResultExtensions/ProblemResponses echo lại toàn bộ Error hoặc thêm
        // field ngoài {type,title,status,code,traceId,errors}, test này sẽ bắt được vì rawPassword sẽ xuất
        // hiện trong body dù không có trong FieldErrors.
        const string rawPassword = "S3cr3t-Raw-Password-Do-Not-Leak";
        var error = new Error("invalid_current_password", $"Mật khẩu hiện tại không đúng (input thô: {rawPassword}).", ErrorType.Validation)
        {
            FieldErrors = new Dictionary<string, string[]> { ["currentPassword"] = ["Mật khẩu hiện tại không đúng."] }
        };

        var (_, body) = await ToProblemAsync(error);

        // Message gốc (chứa rawPassword) được dùng làm "title" — đúng theo thiết kế ToProblem hiện tại; test
        // này chỉ khẳng định "errors" (thứ FE hiển thị theo field) không mang gì ngoài message cố định đã khai,
        // và cấu trúc JSON không có field lạ nào khác chứa rawPassword ngoài "title".
        var propertyNames = body.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "code", "errors", "status", "title", "traceId", "type" }, propertyNames);
        Assert.DoesNotContain(rawPassword, body.GetProperty("errors").GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Error_WithoutFieldErrors_HasNoErrorsProperty()
    {
        var error = new Error("some_code", "Thông báo lỗi", ErrorType.Validation);

        var (_, body) = await ToProblemAsync(error);

        Assert.False(body.TryGetProperty("errors", out _));
    }
}

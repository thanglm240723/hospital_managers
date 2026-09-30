using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.ChangePassword;

public static class ChangePasswordErrors
{
    private const string Code = "validation_failed";
    private const string Title = "Dữ liệu không hợp lệ.";

    public static readonly Error InvalidCurrentPassword = FieldError("currentPassword", "Mật khẩu hiện tại không đúng.");

    public static readonly Error SameAsCurrent = FieldError("newPassword", "Mật khẩu mới phải khác mật khẩu hiện tại.");

    public static readonly Error ContainsEmailName = FieldError("newPassword", "Mật khẩu mới không được chứa tên đăng nhập trong email.");

    private static Error FieldError(string field, string message) => new(Code, Title, ErrorType.Validation)
    {
        FieldErrors = new Dictionary<string, string[]> { [field] = [message] },
    };
}

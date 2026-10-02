using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Users.Common;

/// Lỗi nghiệp vụ của feature Users.
public static class UsersErrors
{
    public static readonly Error NotFound =
        new("user_not_found", "Không tìm thấy tài khoản.", ErrorType.NotFound);

    public static readonly Error EmailTaken =
        new("email_taken", "Email đã được dùng cho tài khoản khác.", ErrorType.Conflict);

    public static readonly Error VersionConflict =
        new("user_version_conflict", "Tài khoản đã được thay đổi bởi người khác. Vui lòng tải lại.", ErrorType.Precondition);

    public static readonly Error SelfActionForbidden =
        new("self_action_forbidden", "Không thể tự khoá tài khoản hoặc tự gỡ vai trò quản trị của chính mình.", ErrorType.Conflict);

    public static readonly Error LastAdmin =
        new("last_admin", "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.", ErrorType.Conflict);

    public static readonly Error UnknownRoles =
        new("role_not_found", "Danh sách vai trò chứa vai trò không tồn tại.", ErrorType.Validation)
        {
            FieldErrors = new Dictionary<string, string[]> { ["roleIds"] = ["Danh sách vai trò chứa vai trò không tồn tại."] },
        };

    /// Tên constraint unique của Users.Email (mặc định EF cho HasIndex(u => u.Email).IsUnique()).
    public const string EmailUniqueConstraint = "IX_Users_Email";
}

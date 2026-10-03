using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.StaffProfiles.Common;

/// Lỗi nghiệp vụ của feature StaffProfiles.
public static class StaffProfilesErrors
{
    public static readonly Error NotFound =
        new("staff_profile_not_found", "Không tìm thấy hồ sơ nhân sự.", ErrorType.NotFound);

    public static readonly Error UserNotFound =
        new("user_not_found", "Không tìm thấy tài khoản.", ErrorType.NotFound);

    public static readonly Error AlreadyExists =
        new("staff_profile_exists", "Tài khoản này đã có hồ sơ nhân sự.", ErrorType.Conflict);

    public static readonly Error CodeTaken =
        new("staff_code_taken", "Mã nhân sự đã được dùng cho người khác.", ErrorType.Conflict);

    public static readonly Error VersionConflict =
        new("staff_profile_version_conflict", "Hồ sơ đã được thay đổi bởi người khác. Vui lòng tải lại.", ErrorType.Precondition);

    public static Error InvalidDepartments(IEnumerable<Guid> ids) =>
        new("invalid_departments", "Có khoa không tồn tại hoặc đã ngừng sử dụng.", ErrorType.Validation)
        {
            FieldErrors = new Dictionary<string, string[]>
            {
                ["departmentIds"] = [$"Khoa không hợp lệ: {string.Join(", ", ids)}"],
            },
        };

    public const string StaffCodeConstraint = "IX_StaffProfiles_StaffCode";
    public const string UserIdConstraint = "IX_StaffProfiles_UserId";
}

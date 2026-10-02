using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Roles.Common;

/// Lỗi nghiệp vụ của feature Roles.
public static class RolesErrors
{
    public static readonly Error NotFound =
        new("role_not_found", "Không tìm thấy vai trò.", ErrorType.NotFound);

    public static readonly Error CodeTaken =
        new("role_code_taken", "Mã vai trò đã tồn tại.", ErrorType.Conflict);

    public static readonly Error VersionConflict =
        new("role_version_conflict", "Vai trò đã được thay đổi bởi người khác. Vui lòng tải lại.", ErrorType.Precondition);

    public static readonly Error AdminCorePermissionsRequired =
        new("admin_core_permissions_required", "Vai trò admin phải giữ đủ quyền quản trị truy cập lõi.", ErrorType.Conflict);

    /// Tên constraint unique của Roles.Code (mặc định EF cho HasIndex(r => r.Code).IsUnique()).
    public const string CodeUniqueConstraint = "IX_Roles_Code";

    public static RoleDto ToDto(Role role) => new(
        role.Id, role.Code, role.Name, role.IsSystem,
        role.GrantedPermissions.Select(p => p.PermissionCode).Order(StringComparer.Ordinal).ToList(),
        role.RowVersion);
}

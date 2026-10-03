namespace QuanLyBenhVien.Domain.Identity;

/// Danh mục permission là code: endpoint tham chiếu hằng nên không gõ sai mã.
/// Seeder đồng bộ danh mục này vào bảng Permissions lúc khởi động.
/// Module nghiệp vụ mới: thêm class lồng + danh sách riêng, rồi nối vào All.
public static class Permissions
{
    public static class Users
    {
        public const string Read = "users.read";
        public const string Create = "users.create";
        public const string Activate = "users.activate";
        public const string ManageRoles = "users.roles.manage";
        public const string ManagePermissions = "users.permissions.manage";
    }

    public static class Roles
    {
        public const string Read = "roles.read";
        public const string Manage = "roles.manage";
    }

    public static class Catalog
    {
        public const string Read = "permissions.read";
    }

    public static class Facilities
    {
        public const string Read = "facilities.read";
        public const string Manage = "facilities.manage";
    }

    public static class StaffProfiles
    {
        public const string Read = "staff-profiles.read";
        public const string Manage = "staff-profiles.manage";
    }

    /// Quyền của module IdentityAccess — seeder gán hết cho role admin.
    public static IReadOnlyList<PermissionDefinition> IdentityAccess { get; } =
    [
        new(Users.Read, "Tài khoản", "Xem danh sách và chi tiết tài khoản"),
        new(Users.Create, "Tài khoản", "Tạo tài khoản"),
        new(Users.Activate, "Tài khoản", "Khoá và mở khoá tài khoản"),
        new(Users.ManageRoles, "Tài khoản", "Gán vai trò cho tài khoản"),
        new(Users.ManagePermissions, "Tài khoản", "Cấp và thu hồi quyền lẻ"),
        new(Roles.Read, "Vai trò", "Xem vai trò và quyền của vai trò"),
        new(Roles.Manage, "Vai trò", "Tạo, đổi tên vai trò và sửa quyền của vai trò"),
        new(Catalog.Read, "Quyền", "Xem danh mục quyền"),
    ];

    /// Quyền của module cơ cấu tổ chức (spec phân quyền hai lớp).
    public static IReadOnlyList<PermissionDefinition> Organization { get; } =
    [
        new(Facilities.Read, "Cơ cấu tổ chức", "Xem cơ sở, khoa, phòng"),
        new(Facilities.Manage, "Cơ cấu tổ chức", "Tạo, đổi tên, ngừng dùng cơ sở, khoa, phòng"),
        new(StaffProfiles.Read, "Nhân sự", "Xem hồ sơ nhân sự và phạm vi làm việc"),
        new(StaffProfiles.Manage, "Nhân sự", "Tạo, sửa hồ sơ nhân sự và gán cơ sở/khoa làm việc"),
    ];

    public static IReadOnlyList<PermissionDefinition> All { get; } = [.. IdentityAccess, .. Organization];

    private static readonly HashSet<string> Codes = All.Select(p => p.Code).ToHashSet(StringComparer.Ordinal);

    public static bool IsDefined(string code) => Codes.Contains(code);
}

namespace QuanLyBenhVien.Persistence.Seed;

/// Lịch sử: cặp (vai trò, quyền) mặc định đã được seeder áp. Có dòng ⇒ không bao giờ áp lại, kể cả khi admin đã bỏ quyền.
internal sealed class RolePermissionDefault
{
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = default!;
    public DateTimeOffset AppliedAt { get; private set; }

    private RolePermissionDefault() { }

    public RolePermissionDefault(Guid roleId, string permissionCode, DateTimeOffset appliedAt)
        => (RoleId, PermissionCode, AppliedAt) = (roleId, permissionCode, appliedAt);
}

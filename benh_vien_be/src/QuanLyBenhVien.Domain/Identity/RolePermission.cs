using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Identity;

public sealed class RolePermission : IAuditable
{
    public Guid RoleId { get; private set; }
    public string PermissionCode { get; private set; } = default!;

    private RolePermission() { }

    internal RolePermission(Guid roleId, string permissionCode)
    {
        RoleId = roleId;
        PermissionCode = permissionCode;
    }
}

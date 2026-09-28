using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Identity;

/// Quyền lẻ cấp thêm ngoài role. Chỉ cấp thêm, không có deny.
public sealed class UserPermission : IAuditable
{
    public Guid UserId { get; private set; }
    public string PermissionCode { get; private set; } = default!;
    public string Reason { get; private set; } = default!;
    public DateTimeOffset GrantedAtUtc { get; private set; }
    public Guid? GrantedBy { get; private set; }

    private UserPermission() { }

    internal UserPermission(Guid userId, string permissionCode, string reason, Guid? grantedBy, DateTimeOffset grantedAtUtc)
    {
        UserId = userId;
        PermissionCode = permissionCode;
        Reason = reason;
        GrantedBy = grantedBy;
        GrantedAtUtc = grantedAtUtc;
    }
}

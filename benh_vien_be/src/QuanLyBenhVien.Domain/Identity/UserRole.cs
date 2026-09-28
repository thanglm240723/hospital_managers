using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Identity;

public sealed class UserRole : IAuditable
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    /// Chừa sẵn cho quyền theo cơ sở — spec này chưa dùng, luôn null.
    public Guid? FacilityId { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public Guid? AssignedBy { get; private set; }

    private UserRole() { }

    internal UserRole(Guid userId, Guid roleId, Guid? assignedBy, DateTimeOffset assignedAtUtc)
    {
        UserId = userId;
        RoleId = roleId;
        AssignedBy = assignedBy;
        AssignedAtUtc = assignedAtUtc;
    }
}

namespace QuanLyBenhVien.Application.Common.Authorization;

public sealed record AccessScope(Guid UserId, Guid? StaffProfileId, IReadOnlySet<Guid> BranchIds, IReadOnlySet<Guid> DepartmentIds)
{
    /// Không có hồ sơ nhân sự đang dùng hoặc không có phạm vi ⇒ không qua được bước 3 với mọi tài nguyên CS/QH.
    public bool HasWorkScope => StaffProfileId is not null && BranchIds.Count > 0;
}

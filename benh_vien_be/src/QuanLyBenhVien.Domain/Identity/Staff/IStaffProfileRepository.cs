namespace QuanLyBenhVien.Domain.Identity.Staff;

public interface IStaffProfileRepository
{
    /// Kèm WorkScopes, tracking.
    Task<StaffProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(StaffProfile profile, CancellationToken ct = default);
    /// Buộc UPDATE StaffProfile (bump xmin) khi chỉ bảng con StaffWorkScopes đổi.
    void MarkChanged(StaffProfile profile);
}

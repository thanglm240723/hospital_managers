namespace QuanLyBenhVien.Domain.Identity;

public interface IRoleRepository
{
    /// Kèm GrantedPermissions, tracking.
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// Kèm GrantedPermissions, tracking.
    Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    /// Khoá hàng role (FOR UPDATE) rồi nạp lại bản mới nhất kèm GrantedPermissions, tracking. PHẢI gọi trong transaction.
    Task<Role?> GetForUpdateAsync(Guid id, CancellationToken ct = default);
    /// Buộc UPDATE bản ghi Role (để xmin/RowVersion đổi) khi chỉ bảng con RolePermissions thay đổi.
    void MarkChanged(Role role);
    Task AddAsync(Role role, CancellationToken ct = default);
}

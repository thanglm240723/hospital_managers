namespace QuanLyBenhVien.Domain.Identity;

public interface IRoleRepository
{
    /// Kèm GrantedPermissions, tracking.
    Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default);
    /// Kèm GrantedPermissions, tracking.
    Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default);
    Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default);
    Task<bool> CodeExistsAsync(string code, CancellationToken ct = default);
    Task AddAsync(Role role, CancellationToken ct = default);
}

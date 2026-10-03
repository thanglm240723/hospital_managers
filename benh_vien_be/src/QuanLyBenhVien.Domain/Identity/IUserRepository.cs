namespace QuanLyBenhVien.Domain.Identity
{
    public interface IUserRepository 
    {
        /// Nạp user theo email — chỉ để định vị Id (KHÔNG tracking). Muốn sửa thì khoá bằng
        /// GetForUpdateAsync trong transaction rồi nạp lại instance tracking.
        Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);
        /// Nạp user theo ID
        Task<User> GetUserByIdAsync(Guid id, CancellationToken ct = default);

        /// Khoá hàng user (FOR UPDATE), nạp lại instance CÓ TRACKING với dữ liệu mới nhất đã commit.
        /// PHẢI gọi bên trong transaction (IUnitOfWork.BeginTransactionAsync) — khoá tự nhả khi transaction commit/rollback.
        Task<User?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

        Task AddUserAsync(User user, CancellationToken ct = default);
        //update trả void chứ ko phải task<User> vì ko cần trả về user
        void UpdateUser(User user);
        Task<bool> EmailExistsAsync(string email , CancellationToken ct = default);

        /// Nạp user kèm RoleAssignments + PermissionGrants (tracking) để sửa quyền.
        Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default);

        /// Khoá hàng user (FOR UPDATE) rồi nạp lại bản mới nhất kèm RoleAssignments + PermissionGrants (tracking).
        /// Instance đã tracking trước đó (vd. lần thử trước bị rollback) bị bỏ để không dùng dữ liệu cũ. PHẢI gọi trong transaction.
        Task<User?> GetWithAccessForUpdateAsync(Guid id, CancellationToken ct = default);

        /// Role hiện có của user — không khoá, không tracking; chỉ để biết role nào cần khoá trước khi khoá User.
        Task<IReadOnlyList<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken ct = default);
        Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default);
        Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default);

        /// Khoá tư vấn (transaction-scoped) để tuần tự hoá các thao tác ảnh hưởng bất biến "còn ≥ 1 admin
        /// đang hoạt động" (khoá tài khoản, gỡ role admin...). PHẢI gọi bên trong transaction
        /// (IUnitOfWork.BeginTransactionAsync) — khoá tự nhả khi transaction commit/rollback.
        Task AcquireAdminSafetyLockAsync(CancellationToken ct = default);
    }
}
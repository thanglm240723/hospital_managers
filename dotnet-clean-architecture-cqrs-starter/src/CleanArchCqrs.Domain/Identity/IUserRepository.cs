namespace CleanArchCqrs.Domain.Identity
{
    public interface IUserRepository 
    {
        /// Nạp user theo email. Trả về instance CÓ TRACKING vì handler login
        /// sẽ gọi RecordLogin() rồi save.
        Task<User?> GetByEmailAsync(string email, CancellationToken ct = default);    
        /// Nạp user theo ID   
        Task<User> GetUserByIdAsync(Guid id, CancellationToken ct = default);

        Task AddUserAsync(User user, CancellationToken ct = default);
        //update trả void chứ ko phải task<User> vì ko cần trả về user
        void UpdateUser(User user);
        Task<bool> EmailExistsAsync(string email , CancellationToken ct = default);

        /// Nạp user kèm RoleAssignments + PermissionGrants (tracking) để sửa quyền.
        Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default);
        Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default);
        Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default);

        /// Khoá tư vấn (transaction-scoped) để tuần tự hoá các thao tác ảnh hưởng bất biến "còn ≥ 1 admin
        /// đang hoạt động" (khoá tài khoản, gỡ role admin...). PHẢI gọi bên trong transaction
        /// (IUnitOfWork.BeginTransactionAsync) — khoá tự nhả khi transaction commit/rollback.
        Task AcquireAdminSafetyLockAsync(CancellationToken ct = default);
    }
}
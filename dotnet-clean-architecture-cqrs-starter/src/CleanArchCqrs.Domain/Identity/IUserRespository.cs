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
    }
}
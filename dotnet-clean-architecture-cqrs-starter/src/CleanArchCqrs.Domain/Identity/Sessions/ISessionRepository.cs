namespace CleanArchCqrs.Domain.Identity.Sessions;

public interface ISessionRepository
{
    /// Tra không khoá, không tracking — chỉ để biết family nào cần khoá.
    Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// Khoá hàng family rồi hàng token (FOR UPDATE), nạp token được trình và token còn dùng được.
    /// PHẢI gọi bên trong transaction (IUnitOfWork.BeginTransactionAsync), nếu không khoá nhả ngay.
    Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default);

    /// Khoá mọi family Active của user theo thứ tự Id, nạp token còn dùng được. Gọi trong transaction.
    Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(SessionFamily family, CancellationToken ct = default);
}

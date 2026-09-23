namespace CleanArchCqrs.Domain.Identity.Sessions;

public interface ISessionRepository
{
    /// Tra không khoá, không tracking — chỉ để biết family nào cần khoá. Trả về family bất kể trạng thái
    /// (kể cả family đã Revoked) — cần thiết để phát hiện reuse trên token đã dùng/đã thu hồi.
    Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// Tra không khoá, không tracking — trả cả trạng thái để caller (vd. lớp chặn CSRF) phân biệt được
    /// family còn Active hay đã Revoked/không tồn tại, mà không cần gọi GetForUpdateAsync (đòi hỏi transaction).
    Task<(Guid FamilyId, SessionStatus Status)?> FindFamilyStatusByTokenHashAsync(string tokenHash, CancellationToken ct = default);

    /// Khoá hàng family rồi hàng token (FOR UPDATE), nạp token được trình và token còn dùng được.
    /// PHẢI gọi bên trong transaction (IUnitOfWork.BeginTransactionAsync), nếu không khoá nhả ngay.
    Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default);

    /// Khoá mọi family Active của user theo thứ tự Id, nạp token còn dùng được. Gọi trong transaction.
    Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default);

    Task AddAsync(SessionFamily family, CancellationToken ct = default);
}

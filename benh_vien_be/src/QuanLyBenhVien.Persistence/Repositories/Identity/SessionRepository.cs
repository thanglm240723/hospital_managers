using QuanLyBenhVien.Domain.Identity.Sessions;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Persistence.Repositories.Identity;

/// Khoá hàng theo Đặc tả kỹ thuật §4.2: luôn family trước, token sau.
/// FOR UPDATE không ghép được với Include của EF, nên khoá bằng câu SELECT riêng rồi mới nạp;
/// ở READ COMMITTED, câu nạp sau khi có khoá luôn thấy dữ liệu đã commit mới nhất.
internal sealed class SessionRepository : ISessionRepository
{
    private readonly AppDbContext _context;

    public SessionRepository(AppDbContext context) => _context = context;

    public async Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => await _context.RefreshTokens.AsNoTracking()
            .Where(t => t.TokenHash == tokenHash)
            .Select(t => (Guid?)t.FamilyId)
            .SingleOrDefaultAsync(ct);

    public async Task<(Guid FamilyId, SessionStatus Status)?> FindFamilyStatusByTokenHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var match = await (
            from t in _context.RefreshTokens.AsNoTracking()
            join f in _context.SessionFamilies.AsNoTracking() on t.FamilyId equals f.Id
            where t.TokenHash == tokenHash
            select new { f.Id, f.Status }
        ).SingleOrDefaultAsync(ct);

        return match is null ? null : (match.Id, match.Status);
    }

    public async Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default)
    {
        EnsureTransaction();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SessionFamilies\" WHERE \"Id\" = {familyId} FOR UPDATE", ct);
        if (presentedTokenHash is not null)
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT 1 FROM \"RefreshTokens\" WHERE \"FamilyId\" = {familyId} AND \"TokenHash\" = {presentedTokenHash} FOR UPDATE", ct);

        return await _context.SessionFamilies
            .Include(f => f.Tokens.Where(t => t.TokenHash == presentedTokenHash
                                           || (t.ConsumedAtUtc == null && t.RevokedAtUtc == null)))
            .SingleOrDefaultAsync(f => f.Id == familyId, ct);
    }

    public async Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default)
    {
        EnsureTransaction();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"SessionFamilies\" WHERE \"UserId\" = {userId} AND \"Status\" = 'Active' ORDER BY \"Id\" FOR UPDATE", ct);

        return await _context.SessionFamilies
            .Include(f => f.Tokens.Where(t => t.ConsumedAtUtc == null && t.RevokedAtUtc == null))
            .Where(f => f.UserId == userId && f.Status == SessionStatus.Active)
            .OrderBy(f => f.Id)
            .ToListAsync(ct);
    }

    public async Task AddAsync(SessionFamily family, CancellationToken ct = default)
        => await _context.SessionFamilies.AddAsync(family, ct);

    private void EnsureTransaction()
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks require an open transaction (IUnitOfWork.BeginTransactionAsync).");
    }
}

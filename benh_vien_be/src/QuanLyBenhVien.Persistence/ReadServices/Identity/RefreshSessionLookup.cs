using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

internal sealed class RefreshSessionLookup(AppDbContext context) : IRefreshSessionLookup
{
    public async Task<RefreshSessionRef?> FindAsync(string tokenHash, CancellationToken ct = default)
    {
        var match = await (
            from t in context.RefreshTokens.AsNoTracking()
            join f in context.SessionFamilies.AsNoTracking() on t.FamilyId equals f.Id
            where t.TokenHash == tokenHash
            select new { f.Id, f.UserId, f.Status }
        ).SingleOrDefaultAsync(ct);

        return match is null ? null : new RefreshSessionRef(match.Id, match.UserId, match.Status);
    }
}

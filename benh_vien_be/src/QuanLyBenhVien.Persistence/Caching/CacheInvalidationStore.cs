using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Common.Caching;

namespace QuanLyBenhVien.Persistence.Caching;

internal sealed class CacheInvalidationStore : ICacheInvalidationStore
{
    private const int MaxErrorLength = 1000;

    private readonly AppDbContext _db;

    public CacheInvalidationStore(AppDbContext db) => _db = db;

    public Guid Enqueue(string key, DateTimeOffset now)
    {
        var row = CacheInvalidation.Create(key, now);
        _db.CacheInvalidations.Add(row);
        return row.Id;
    }

    public async Task<IReadOnlyList<CacheInvalidationWorkItem>> ClaimAsync(
        IReadOnlyCollection<Guid>? ids, int take, Guid claimId, DateTimeOffset now, TimeSpan lease, CancellationToken ct)
    {
        if (_db.Database.CurrentTransaction is not null)
            throw new InvalidOperationException("Cache invalidation claims must not run inside a business transaction.");
        if (take <= 0 || ids is { Count: 0 }) return [];

        var until = now + lease;
        var filterIds = ids?.ToArray();

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        var rows = filterIds is null
            ? await _db.CacheInvalidations.FromSqlInterpolated($"""
                WITH picked AS (
                    SELECT "Id" FROM "CacheInvalidations"
                    WHERE "ClaimedUntilUtc" IS NULL OR "ClaimedUntilUtc" <= {now}
                    ORDER BY "CreatedAtUtc", "Id"
                    LIMIT {take}
                    FOR UPDATE SKIP LOCKED)
                UPDATE "CacheInvalidations" c SET "ClaimId" = {claimId}, "ClaimedUntilUtc" = {until}
                FROM picked WHERE c."Id" = picked."Id"
                RETURNING c.*
                """).AsNoTracking().ToListAsync(ct)
            : await _db.CacheInvalidations.FromSqlInterpolated($"""
                WITH picked AS (
                    SELECT "Id" FROM "CacheInvalidations"
                    WHERE "Id" = ANY({filterIds})
                      AND ("ClaimedUntilUtc" IS NULL OR "ClaimedUntilUtc" <= {now})
                    ORDER BY "CreatedAtUtc", "Id"
                    LIMIT {take}
                    FOR UPDATE SKIP LOCKED)
                UPDATE "CacheInvalidations" c SET "ClaimId" = {claimId}, "ClaimedUntilUtc" = {until}
                FROM picked WHERE c."Id" = picked."Id"
                RETURNING c.*
                """).AsNoTracking().ToListAsync(ct);
        await tx.CommitAsync(ct);

        return rows.OrderBy(r => r.CreatedAtUtc).ThenBy(r => r.Id)
            .Select(r => new CacheInvalidationWorkItem(r.Id, r.Key, r.Attempts)).ToList();
    }

    public Task<int> CompleteAsync(IReadOnlyCollection<Guid> ids, Guid claimId, CancellationToken ct)
        => _db.CacheInvalidations
            .Where(r => ids.Contains(r.Id) && r.ClaimId == claimId)
            .ExecuteDeleteAsync(ct);

    public Task<int> FailAsync(IReadOnlyCollection<Guid> ids, Guid claimId, string safeError, CancellationToken ct)
    {
        var error = safeError.Length > MaxErrorLength ? safeError[..MaxErrorLength] : safeError;
        return _db.CacheInvalidations
            .Where(r => ids.Contains(r.Id) && r.ClaimId == claimId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.Attempts, r => r.Attempts + 1)
                .SetProperty(r => r.LastError, error)
                .SetProperty(r => r.ClaimId, (Guid?)null)
                .SetProperty(r => r.ClaimedUntilUtc, (DateTimeOffset?)null), ct);
    }
}

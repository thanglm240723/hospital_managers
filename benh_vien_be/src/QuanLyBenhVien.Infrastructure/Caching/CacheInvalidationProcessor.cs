using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Caching;

public sealed class CacheInvalidationProcessor
{
    private const int AlertAfterAttempts = 10;

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CacheInvalidationProcessor> _logger;

    public CacheInvalidationProcessor(AppDbContext db, IConnectionMultiplexer redis, ILogger<CacheInvalidationProcessor> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var rows = await _db.CacheInvalidations.OrderBy(r => r.CreatedAtUtc).Take(batchSize).ToListAsync(ct);
        if (rows.Count == 0) return 0;

        try
        {
            await GuardedCacheWrite.InvalidateAsync(_redis.GetDatabase(), rows.Select(r => r.Key).ToList());
            _db.CacheInvalidations.RemoveRange(rows);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            foreach (var row in rows) row.MarkFailed(ex.Message);
            var level = rows.Any(r => r.Attempts >= AlertAfterAttempts) ? LogLevel.Error : LogLevel.Warning;
            _logger.Log(level, ex, "Could not invalidate {Count} cache keys; will retry", rows.Count);
        }

        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }
}

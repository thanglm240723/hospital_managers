using QuanLyBenhVien.Application.Common.Interfaces;
using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Caching;

public sealed class CacheInvalidator : ICacheInvalidator
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<CacheInvalidator> _logger;
    private readonly List<CacheInvalidation> _pending = new();

    public CacheInvalidator(AppDbContext db, IConnectionMultiplexer redis, TimeProvider time, ILogger<CacheInvalidator> logger)
    {
        _db = db;
        _redis = redis;
        _time = time;
        _logger = logger;
    }

    public void InvalidateSession(Guid sessionFamilyId) => Enqueue(CacheKeys.Session(sessionFamilyId));

    public void InvalidatePermissions(Guid userId) => Enqueue(CacheKeys.Permissions(userId));

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0) return;
        var batch = _pending.ToList();
        _pending.Clear();
        try
        {
            await GuardedCacheWrite.InvalidateAsync(_redis.GetDatabase(), batch.Select(r => r.Key).ToList());
            _db.CacheInvalidations.RemoveRange(batch);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (RedisFailure.Is(ex) || ex is DbUpdateException)
        {
            _logger.LogWarning(ex, "Deferred {Count} cache invalidations to the background worker", batch.Count);
        }
    }

    private void Enqueue(string key)
    {
        if (_pending.Any(p => p.Key == key)) return;
        var row = CacheInvalidation.Create(key, _time.GetUtcNow());
        _db.CacheInvalidations.Add(row);
        _pending.Add(row);
    }
}

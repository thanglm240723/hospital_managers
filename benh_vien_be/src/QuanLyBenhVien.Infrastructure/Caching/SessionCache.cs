using QuanLyBenhVien.Application.Common.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Caching;

public sealed class SessionCache : ISessionCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionCache> _logger;

    public SessionCache(IConnectionMultiplexer redis, TimeProvider time, ILogger<SessionCache> logger)
    {
        _redis = redis;
        _time = time;
        _logger = logger;
    }

    public async Task<CacheGeneration?> ReadGenerationAsync(Guid sessionFamilyId, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var value = await GuardedCacheWrite.ReadGenerationAsync(db, CacheKeys.Session(sessionFamilyId));
            return new CacheGeneration(value.IsNull ? null : (string?)value);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not read session cache generation {SessionFamilyId}", sessionFamilyId);
            return null;
        }
    }

    public async Task SetIfGenerationUnchangedAsync(SessionCacheEntry entry, CacheGeneration expected, CancellationToken ct = default)
    {
        var ttl = entry.AbsoluteExpiresAtUtc - _time.GetUtcNow();
        if (ttl <= TimeSpan.Zero) return;
        try
        {
            var db = _redis.GetDatabase();
            await GuardedCacheWrite.SetIfUnchangedAsync(
                db,
                CacheKeys.Session(entry.SessionFamilyId),
                SessionCachePayload.Serialize(entry.UserId, entry.SecurityVersion, entry.AbsoluteExpiresAtUtc),
                expected.Value is null ? RedisValue.Null : expected.Value,
                ttl);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not cache session {SessionFamilyId}; gateway will fall back to the API", entry.SessionFamilyId);
        }
    }
}

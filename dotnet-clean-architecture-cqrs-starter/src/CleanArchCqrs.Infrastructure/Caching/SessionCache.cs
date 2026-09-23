using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

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

    public async Task SetAsync(SessionCacheEntry entry, CancellationToken ct = default)
    {
        var ttl = entry.AbsoluteExpiresAtUtc - _time.GetUtcNow();
        if (ttl <= TimeSpan.Zero) return;
        try
        {
            await _redis.GetDatabase().StringSetAsync(
                CacheKeys.Session(entry.SessionFamilyId), SessionCachePayload.Serialize(entry), ttl);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not cache session {SessionFamilyId}; gateway will fall back to the API", entry.SessionFamilyId);
        }
    }
}

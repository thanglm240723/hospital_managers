using QuanLyBenhVien.Application.Common.Caching;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Caching;

internal sealed class RedisCacheKeyEvictor : ICacheKeyEvictor
{
    private readonly IConnectionMultiplexer _redis;

    public RedisCacheKeyEvictor(IConnectionMultiplexer redis) => _redis = redis;

    public Task EvictAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
        => GuardedCacheWrite.InvalidateAsync(_redis.GetDatabase(), keys).WaitAsync(ct);
}

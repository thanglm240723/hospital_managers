using QuanLyBenhVien.Application.Common.Interfaces;
using QuanLyBenhVien.Application.Common.Models;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Identity;

/// perm:{userId} không TTL; đúng đắn nhờ (1) xoá chủ động qua CacheInvalidations và (2) ghi có điều kiện theo thế hệ.
public sealed class PermissionService : IPermissionService
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PermissionService> _logger;
    private readonly Dictionary<Guid, UserAccess> _perRequest = new();

    public PermissionService(AppDbContext db, IConnectionMultiplexer redis, ILogger<PermissionService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<UserAccess> GetAsync(Guid userId, CancellationToken ct = default)
    {
        if (_perRequest.TryGetValue(userId, out var known)) return known;

        var key = CacheKeys.Permissions(userId);
        var (cached, generation) = await TryReadAsync(key);
        var access = cached ?? await EffectivePermissionsQuery.LoadAsync(_db, userId, ct) ?? UserAccess.None;

        // Không cache user không tồn tại (None); user bị khoá vẫn cache tập rỗng.
        if (cached is null && generation is { } readGeneration && !ReferenceEquals(access, UserAccess.None))
            await TryWriteAsync(key, access, readGeneration);

        _perRequest[userId] = access;
        return access;
    }

    /// Generation = null nghĩa là Redis lỗi ⇒ không ghi cache.
    private async Task<(UserAccess? Access, RedisValue? Generation)> TryReadAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var generation = await GuardedCacheWrite.ReadGenerationAsync(db, key);   // TRƯỚC khi đọc DB
            var value = await db.StringGetAsync(key);
            return (value.HasValue ? PermissionCachePayload.Deserialize(value.ToString()) : null, generation);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable; reading permissions from the database");
            return (null, null);
        }
    }

    private async Task TryWriteAsync(string key, UserAccess access, RedisValue generation)
    {
        try
        {
            await GuardedCacheWrite.SetIfUnchangedAsync(_redis.GetDatabase(), key, PermissionCachePayload.Serialize(access), generation, expiry: null);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not cache permissions");
        }
    }
}

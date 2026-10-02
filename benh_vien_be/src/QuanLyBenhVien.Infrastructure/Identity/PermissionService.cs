using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Infrastructure.Caching;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Identity;

/// perm:{userId} không TTL; đúng đắn nhờ (1) xoá chủ động qua CacheInvalidations và (2) ghi có điều kiện theo thế hệ.
/// DB chỉ qua port IUserAccessReadService; DB lỗi thì ném exception (không dùng quyền cũ).
internal sealed class PermissionService(
    IUserAccessReadService reader, IConnectionMultiplexer redis, ILogger<PermissionService> logger) : IPermissionService
{
    private readonly Dictionary<Guid, UserAccessDto?> _perRequest = new();

    public async Task<UserAccessDto?> GetAsync(Guid userId, CancellationToken ct)
    {
        if (_perRequest.TryGetValue(userId, out var known)) return known;

        var key = CacheKeys.Permissions(userId);
        var (cached, generation) = await TryReadAsync(key);
        var access = cached ?? await reader.GetAsync(userId, ct);

        // Không cache user không tồn tại; user bị khoá vẫn cache tập rỗng.
        if (cached is null && access is not null && generation is { } readGeneration)
            await TryWriteAsync(key, access, readGeneration);

        _perRequest[userId] = access;
        return access;
    }

    /// Generation = null nghĩa là Redis lỗi, không ghi cache.
    private async Task<(UserAccessDto? Access, RedisValue? Generation)> TryReadAsync(string key)
    {
        try
        {
            var db = redis.GetDatabase();
            var generation = await GuardedCacheWrite.ReadGenerationAsync(db, key);   // TRƯỚC khi đọc DB
            var value = await db.StringGetAsync(key);
            if (!value.HasValue) return (null, generation);

            var access = PermissionCachePayload.TryDeserialize(value.ToString());
            if (access is null) logger.LogWarning("Permission cache payload is invalid; reading from the database");
            return (access, generation);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            logger.LogWarning(ex, "Redis unavailable; reading permissions from the database");
            return (null, null);
        }
    }

    private async Task TryWriteAsync(string key, UserAccessDto access, RedisValue generation)
    {
        try
        {
            await GuardedCacheWrite.SetIfUnchangedAsync(redis.GetDatabase(), key, PermissionCachePayload.Serialize(access), generation, expiry: null);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            logger.LogWarning(ex, "Could not cache permissions");
        }
    }
}

using QuanLyBenhVien.Application.Auth.Models;
using QuanLyBenhVien.Application.Common.Interfaces;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Identity;

public sealed class SessionValidationService : ISessionValidationService
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionValidationService> _logger;

    public SessionValidationService(AppDbContext db, IConnectionMultiplexer redis, TimeProvider time,
        ILogger<SessionValidationService> logger)
    {
        _db = db;
        _redis = redis;
        _time = time;
        _logger = logger;
    }

    public async Task<SessionValidationResult> ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default)
    {
        var key = CacheKeys.Session(sessionFamilyId);
        var generation = await TryReadGenerationAsync(key);   // TRƯỚC khi đọc DB

        var row = await _db.SessionFamilies.AsNoTracking()
            .Where(f => f.Id == sessionFamilyId)
            .Join(_db.Users, f => f.UserId, u => u.Id,
                (f, u) => new { f.Status, f.AbsoluteExpiresAtUtc, UserId = u.Id, u.IsActive, u.SecurityVersion })
            .SingleOrDefaultAsync(ct);

        var now = _time.GetUtcNow();
        if (row is null || row.Status != SessionStatus.Active || row.AbsoluteExpiresAtUtc <= now
            || !row.IsActive || row.SecurityVersion != securityVersion)
            return SessionValidationResult.Invalid;

        if (generation is { } readGeneration)
            await TryRecacheAsync(key, SessionCachePayload.Serialize(row.UserId, row.SecurityVersion, row.AbsoluteExpiresAtUtc),
                readGeneration, row.AbsoluteExpiresAtUtc - now);

        return new SessionValidationResult(true, row.UserId, row.AbsoluteExpiresAtUtc);
    }

    /// null = Redis lỗi ⇒ bỏ qua bước ghi cache.
    private async Task<RedisValue?> TryReadGenerationAsync(string key)
    {
        try
        {
            return await GuardedCacheWrite.ReadGenerationAsync(_redis.GetDatabase(), key);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable while validating session");
            return null;
        }
    }

    private async Task TryRecacheAsync(string key, string payload, RedisValue generation, TimeSpan ttl)
    {
        try
        {
            await GuardedCacheWrite.SetIfUnchangedAsync(_redis.GetDatabase(), key, payload, generation, ttl);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not re-cache session");
        }
    }
}

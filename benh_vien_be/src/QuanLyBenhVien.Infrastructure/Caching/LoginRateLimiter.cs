using QuanLyBenhVien.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Caching;

/// Chặn tạm theo email chuẩn hoá. Không đổi gì trong DB, nên kẻ xấu không khoá vĩnh viễn được tài khoản người khác.
public sealed class LoginRateLimiter : ILoginRateLimiter
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<LoginRateLimiter> _logger;

    public LoginRateLimiter(IConnectionMultiplexer redis, ILogger<LoginRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = CacheKeys.LoginFailures(normalizedEmail);
            var count = await db.StringGetAsync(key);
            if (!count.HasValue || (long)count < MaxFailures) return null;
            return await db.KeyTimeToLiveAsync(key) ?? Window;
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Login rate limiter unavailable; allowing attempt");
            return null;
        }
    }

    public async Task RegisterFailureAsync(string normalizedEmail, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = CacheKeys.LoginFailures(normalizedEmail);
            // INCR + EXPIRE(NX) phải nguyên tử: nếu crash xảy ra giữa hai lệnh riêng lẻ, key mất TTL
            // và GetLockoutRemainingAsync coi như khoá vĩnh viễn (không bao giờ tự hết hạn).
            var transaction = db.CreateTransaction();
            _ = transaction.StringIncrementAsync(key);
            _ = transaction.KeyExpireAsync(key, Window, ExpireWhen.HasNoExpiry);
            await transaction.ExecuteAsync();
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Login rate limiter unavailable; failure not counted");
        }
    }

    public async Task ResetAsync(string normalizedEmail, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(CacheKeys.LoginFailures(normalizedEmail));
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Login rate limiter unavailable; counter not reset");
        }
    }
}

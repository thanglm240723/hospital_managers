using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Caching;

/// Viết lại (không chuyển) từ `RedisServicesTests.cs` (khung cũ, `ISessionCache.SetAsync`/`ILoginRateLimiter`)
/// theo port mới `ISessionCache.SetIfGenerationUnchangedAsync`/`ILoginAttemptLimiter` (Application.Common.Caching,
/// Features.Auth.Common). Chỉ phủ LoginRateLimiter và SessionCache — CacheInvalidator/worker chờ slice riêng.
[Collection(IntegrationCollection.Name)]
public class LoginAndSessionCacheTests
{
    private static readonly Dictionary<string, string?> RedisDown = new() { ["ConnectionStrings:Redis"] = "127.0.0.1:1" };
    private readonly ContainersFixture _containers;

    public LoginAndSessionCacheTests(ContainersFixture containers) => _containers = containers;

    private Task<ApiFactory> CreateAsync(Dictionary<string, string?>? overrides = null)
        => ApiFactory.CreateAsync(_containers, overrides);

    private static IDatabase Redis(ApiFactory f) => f.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    [Fact]
    public async Task LoginRateLimiter_LocksAfterFiveFailuresAndResetClears()
    {
        await using var factory = await CreateAsync();
        var limiter = factory.Services.GetRequiredService<ILoginAttemptLimiter>();
        var email = $"rl-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 4; i++) await limiter.RegisterFailureAsync(email);
        Assert.Null(await limiter.GetLockoutRemainingAsync(email));

        await limiter.RegisterFailureAsync(email);
        Assert.InRange((await limiter.GetLockoutRemainingAsync(email))!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

        await limiter.ResetAsync(email);
        Assert.Null(await limiter.GetLockoutRemainingAsync(email));
    }

    [Fact]
    public async Task LoginRateLimiter_RedisDown_NeverLocks()
    {
        await using var factory = await CreateAsync(RedisDown);
        var limiter = factory.Services.GetRequiredService<ILoginAttemptLimiter>();
        var email = $"rl-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 6; i++) await limiter.RegisterFailureAsync(email);

        Assert.Null(await limiter.GetLockoutRemainingAsync(email));
    }

    [Fact]
    public async Task SessionCache_SetIfGenerationUnchanged_WritesWhenNoGenerationKeyExists()
    {
        await using var factory = await CreateAsync();
        var cache = factory.Services.GetRequiredService<ISessionCache>();
        var entry = new SessionCacheEntry(Guid.NewGuid(), Guid.NewGuid(), 3, DateTimeOffset.UtcNow.AddHours(1));

        await cache.SetIfGenerationUnchangedAsync(entry, CacheGeneration.None);

        Assert.True(await Redis(factory).KeyExistsAsync(CacheKeys.Session(entry.SessionFamilyId)));
    }

    [Fact]
    public async Task SessionCache_SetIfGenerationUnchanged_None_DoesNotWriteWhenGenerationKeyExists()
    {
        // Mô phỏng: admin đổi/thu hồi phiên giữa lúc handler đọc DB và ghi cache -> {fid}:gen đã tồn tại
        // (do GuardedCacheWrite.InvalidateAsync) trước khi handler ghi với "expected = None" (như thể chưa từng đọc).
        await using var factory = await CreateAsync();
        var cache = factory.Services.GetRequiredService<ISessionCache>();
        var entry = new SessionCacheEntry(Guid.NewGuid(), Guid.NewGuid(), 3, DateTimeOffset.UtcNow.AddHours(1));
        var sessionKey = CacheKeys.Session(entry.SessionFamilyId);
        await GuardedCacheWrite.InvalidateAsync(Redis(factory), [sessionKey]);
        Assert.True(await Redis(factory).KeyExistsAsync(CacheKeys.Generation(sessionKey)));

        await cache.SetIfGenerationUnchangedAsync(entry, CacheGeneration.None);

        Assert.False(await Redis(factory).KeyExistsAsync(sessionKey));
    }
}

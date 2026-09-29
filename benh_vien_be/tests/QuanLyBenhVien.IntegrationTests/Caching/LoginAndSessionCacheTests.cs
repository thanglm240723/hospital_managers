using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Caching;

/// Viết lại (không chuyển) từ `RedisServicesTests.cs` (khung cũ, `ISessionCache.SetAsync`) theo port mới
/// `ISessionCache.SetIfGenerationUnchangedAsync` (Application.Common.Caching). Bộ đếm khoá tạm theo email
/// (`ILoginAttemptLimiter`/`LoginRateLimiter`) đã bị gỡ bỏ (quyết định 2026-09-30) — chỉ còn phủ SessionCache.
[Collection(IntegrationCollection.Name)]
public class LoginAndSessionCacheTests
{
    private readonly ContainersFixture _containers;

    public LoginAndSessionCacheTests(ContainersFixture containers) => _containers = containers;

    private Task<ApiFactory> CreateAsync(Dictionary<string, string?>? overrides = null)
        => ApiFactory.CreateAsync(_containers, overrides);

    private static IDatabase Redis(ApiFactory f) => f.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

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

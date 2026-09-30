using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Caching;

/// `RedisCacheKeyEvictor` trên Redis thật: xoá key + tăng `:gen` + TTL, và chặn ghi lại dữ liệu cũ (spec cũ §4.3).
[Collection(IntegrationCollection.Name)]
public class CacheGenerationRaceTests
{
    private readonly ContainersFixture _containers;

    public CacheGenerationRaceTests(ContainersFixture containers) => _containers = containers;

    private Task<ApiFactory> CreateAsync()
        => ApiFactory.CreateAsync(_containers, configureServices: TestServices.RemoveCacheInvalidationWorker);

    private static IDatabase Redis(ApiFactory f) => f.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    [Fact]
    public async Task Evict_DeletesKeysAndIncrementsGenerationWithTtl()
    {
        await using var factory = await CreateAsync();
        var evictor = factory.Services.GetRequiredService<ICacheKeyEvictor>();
        var keys = new[] { $"perm:{Guid.NewGuid()}", $"session:{Guid.NewGuid()}" };
        foreach (var key in keys)
            await Redis(factory).StringSetAsync(key, "v");

        await evictor.EvictAsync(keys, CancellationToken.None);
        await evictor.EvictAsync(keys, CancellationToken.None); // idempotent: chạy lại sau crash vẫn an toàn

        foreach (var key in keys)
        {
            Assert.False(await Redis(factory).KeyExistsAsync(key));
            Assert.Equal("2", (string?)await Redis(factory).StringGetAsync(CacheKeys.Generation(key)));
            var ttl = await Redis(factory).KeyTimeToLiveAsync(CacheKeys.Generation(key));
            Assert.NotNull(ttl);
            Assert.InRange(ttl!.Value, TimeSpan.FromHours(23), TimeSpan.FromDays(1));
        }
    }

    [Fact]
    public async Task WriterHoldingOldGeneration_CannotRepopulateAfterEviction()
    {
        await using var factory = await CreateAsync();
        var cache = factory.Services.GetRequiredService<ISessionCache>();
        var evictor = factory.Services.GetRequiredService<ICacheKeyEvictor>();
        var entry = new SessionCacheEntry(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddHours(1));
        var key = CacheKeys.Session(entry.SessionFamilyId);

        // Writer A đọc thế hệ (chưa có) rồi "đọc DB" chậm; giữa lúc đó thu hồi phiên và evict.
        var generationSeenByWriter = await cache.ReadGenerationAsync(entry.SessionFamilyId);
        Assert.NotNull(generationSeenByWriter);
        await evictor.EvictAsync([key], CancellationToken.None);
        await cache.SetIfGenerationUnchangedAsync(entry, generationSeenByWriter!);
        Assert.False(await Redis(factory).KeyExistsAsync(key));

        // Writer B đọc thế hệ sau eviction, rồi eviction khác chen vào: cũng không được ghi.
        var second = await cache.ReadGenerationAsync(entry.SessionFamilyId);
        await evictor.EvictAsync([key], CancellationToken.None);
        await cache.SetIfGenerationUnchangedAsync(entry, second!);
        Assert.False(await Redis(factory).KeyExistsAsync(key));

        // Thế hệ hiện tại thì được ghi.
        var current = await cache.ReadGenerationAsync(entry.SessionFamilyId);
        await cache.SetIfGenerationUnchangedAsync(entry, current!);
        Assert.True(await Redis(factory).KeyExistsAsync(key));
    }

    [Fact]
    public async Task ConcurrentEvictions_NeverLoseAGenerationIncrement()
    {
        await using var factory = await CreateAsync();
        var evictor = factory.Services.GetRequiredService<ICacheKeyEvictor>();
        var key = $"perm:{Guid.NewGuid()}";

        await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => evictor.EvictAsync([key], CancellationToken.None)));

        Assert.Equal("50", (string?)await Redis(factory).StringGetAsync(CacheKeys.Generation(key)));
        Assert.NotNull(await Redis(factory).KeyTimeToLiveAsync(CacheKeys.Generation(key)));
    }
}

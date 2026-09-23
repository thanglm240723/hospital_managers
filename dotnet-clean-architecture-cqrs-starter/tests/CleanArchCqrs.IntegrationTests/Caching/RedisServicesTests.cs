using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Caching;

[Collection(IntegrationCollection.Name)]
public class RedisServicesTests
{
    private static readonly Dictionary<string, string?> RedisDown = new() { ["ConnectionStrings:Redis"] = "127.0.0.1:1" };
    private readonly ContainersFixture _containers;

    public RedisServicesTests(ContainersFixture containers) => _containers = containers;

    private Task<ApiFactory> CreateAsync(Dictionary<string, string?>? overrides = null)
        => ApiFactory.CreateAsync(_containers, overrides, TestServices.RemoveCacheInvalidationWorker);

    private static IDatabase Redis(ApiFactory f) => f.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    [Fact]
    public async Task SessionCache_WritesGatewayJsonWithTtlUntilAbsoluteExpiry()
    {
        await using var factory = await CreateAsync();
        var entry = new SessionCacheEntry(Guid.NewGuid(), Guid.NewGuid(), 3, DateTimeOffset.UtcNow.AddHours(1));

        await factory.Services.GetRequiredService<ISessionCache>().SetAsync(entry);

        var key = CacheKeys.Session(entry.SessionFamilyId);
        using var json = JsonDocument.Parse((string)(await Redis(factory).StringGetAsync(key))!);
        Assert.Equal(entry.UserId.ToString(), json.RootElement.GetProperty("userId").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("sv").GetInt32());
        Assert.Equal(entry.AbsoluteExpiresAtUtc.ToUnixTimeSeconds(), json.RootElement.GetProperty("absExp").GetInt64());
        Assert.InRange((await Redis(factory).KeyTimeToLiveAsync(key))!.Value, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task SessionCache_RedisDown_DoesNotThrow()
    {
        await using var factory = await CreateAsync(RedisDown);

        await factory.Services.GetRequiredService<ISessionCache>()
            .SetAsync(new SessionCacheEntry(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task LoginRateLimiter_LocksAfterFiveFailuresAndResetClears()
    {
        await using var factory = await CreateAsync();
        var limiter = factory.Services.GetRequiredService<ILoginRateLimiter>();
        var email = $"rl-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 4; i++) await limiter.RegisterFailureAsync(email);
        Assert.Null(await limiter.GetLockoutRemainingAsync(email));

        await limiter.RegisterFailureAsync(email);
        Assert.InRange((await limiter.GetLockoutRemainingAsync(email))!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

        await limiter.ResetAsync(email);
        Assert.Null(await limiter.GetLockoutRemainingAsync(email));
    }

    [Fact]
    public async Task LoginRateLimiter_RegisterFailure_SetsTtlAtomicallyWithCount()
    {
        // INCR và EXPIRE(NX) chạy trong một transaction Redis; ngay sau lệnh gọi đầu tiên,
        // key đếm lỗi phải đã có TTL — nếu không nguyên tử, một crash giữa hai lệnh có thể để lại
        // key không TTL và khoá vĩnh viễn.
        await using var factory = await CreateAsync();
        var limiter = factory.Services.GetRequiredService<ILoginRateLimiter>();
        var email = $"rl-{Guid.NewGuid():N}@test.local";

        await limiter.RegisterFailureAsync(email);

        var ttl = await Redis(factory).KeyTimeToLiveAsync(CacheKeys.LoginFailures(email));
        Assert.NotNull(ttl);
        Assert.InRange(ttl!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));
    }

    [Fact]
    public async Task LoginRateLimiter_RedisDown_NeverLocks()
    {
        await using var factory = await CreateAsync(RedisDown);
        var limiter = factory.Services.GetRequiredService<ILoginRateLimiter>();

        for (var i = 0; i < 6; i++) await limiter.RegisterFailureAsync("x@test.local");

        Assert.Null(await limiter.GetLockoutRemainingAsync("x@test.local"));
    }

    [Fact]
    public async Task CacheInvalidator_FlushAfterCommit_DeletesKeyRowAndBumpsGeneration()
    {
        await using var factory = await CreateAsync();
        var userId = Guid.NewGuid();
        var key = CacheKeys.Permissions(userId);
        await Redis(factory).StringSetAsync(key, "stale");

        await using var scope = factory.Services.CreateAsyncScope();
        var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        invalidator.InvalidatePermissions(userId);
        await db.SaveChangesAsync();
        await invalidator.FlushAsync();

        Assert.False(await Redis(factory).KeyExistsAsync(key));
        Assert.Equal(1, (int)await Redis(factory).StringGetAsync(CacheKeys.Generation(key)));
        Assert.False(await db.CacheInvalidations.AnyAsync(c => c.Key == key));
    }

    [Fact]
    public async Task CacheInvalidator_RedisDown_KeepsRowAndProcessorCountsAttempt()
    {
        await using var factory = await CreateAsync(RedisDown);
        await using var scope = factory.Services.CreateAsyncScope();
        var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var familyId = Guid.NewGuid();
        var key = CacheKeys.Session(familyId);

        invalidator.InvalidateSession(familyId);
        await db.SaveChangesAsync();
        await invalidator.FlushAsync();
        await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>().ProcessPendingAsync(100);

        var row = await db.CacheInvalidations.AsNoTracking().SingleAsync(c => c.Key == key);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.LastError);
    }

    [Fact]
    public async Task Processor_DeletesPendingKeys()
    {
        await using var factory = await CreateAsync();
        var key = CacheKeys.Permissions(Guid.NewGuid());
        await Redis(factory).StringSetAsync(key, "stale");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CacheInvalidations.Add(CacheInvalidation.Create(key, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var processed = await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>().ProcessPendingAsync(100);

        Assert.Equal(1, processed);
        Assert.False(await Redis(factory).KeyExistsAsync(key));
        Assert.False(await db.CacheInvalidations.AsNoTracking().AnyAsync(c => c.Key == key));
    }

    [Fact]
    public async Task GuardedWrite_SkipsWriteWhenKeyWasInvalidatedAfterGenerationRead()
    {
        await using var factory = await CreateAsync();
        var redis = Redis(factory);
        var key = CacheKeys.Permissions(Guid.NewGuid());

        var generation = await GuardedCacheWrite.ReadGenerationAsync(redis, key);   // trước khi đọc DB
        await GuardedCacheWrite.InvalidateAsync(redis, [key]);                        // admin đổi quyền giữa chừng
        var written = await GuardedCacheWrite.SetIfUnchangedAsync(redis, key, "stale", generation, null);

        Assert.False(written);
        Assert.False(await redis.KeyExistsAsync(key));
    }
}

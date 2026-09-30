using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Persistence;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Caching;

/// Bảng chờ `CacheInvalidations` + lease (spec V2 §5.2) trên PostgreSQL/Redis thật.
[Collection(IntegrationCollection.Name)]
public class CacheInvalidationDeliveryTests
{
    private static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);
    private readonly ContainersFixture _containers;

    public CacheInvalidationDeliveryTests(ContainersFixture containers) => _containers = containers;

    private Task<ApiFactory> CreateAsync(Action<IServiceCollection>? extra = null)
        => ApiFactory.CreateAsync(_containers, configureServices: services =>
        {
            TestServices.RemoveCacheInvalidationWorker(services);
            extra?.Invoke(services);
        });

    private static IDatabase Redis(ApiFactory f) => f.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private static async Task<List<Guid>> SeedRowsAsync(ApiFactory f, int count, DateTimeOffset now)
    {
        using var scope = f.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICacheInvalidationStore>();
        var ids = new List<Guid>();
        for (var i = 0; i < count; i++)
            ids.Add(store.Enqueue($"perm:{Guid.NewGuid()}", now.AddMilliseconds(i)));
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
        return ids;
    }

    private static async Task<List<(Guid Id, Guid? ClaimId, int Attempts, string? LastError)>> RowsAsync(ApiFactory f)
    {
        await using var db = TestDb.Create(f.DatabaseConnectionString);
        return (await db.CacheInvalidations.AsNoTracking().ToListAsync())
            .Select(r => (r.Id, r.ClaimId, r.Attempts, r.LastError)).ToList();
    }

    [Fact]
    public async Task Rollback_RemovesAuditAndInvalidationTogether()
    {
        await using var factory = await CreateAsync();
        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            await using var tx = await uow.BeginTransactionAsync();
            sp.GetRequiredService<IAuditWriter>().Record("test.rollback", AuditResult.Succeeded);
            sp.GetRequiredService<ICacheInvalidator>().InvalidateSession(Guid.NewGuid());
            await uow.SaveChangesAsync();
            // dispose không commit ⇒ rollback
        }

        await using var db = TestDb.Create(factory.DatabaseConnectionString);
        Assert.Equal(0, await db.CacheInvalidations.CountAsync());
        Assert.Equal(0, await db.AuditRecords.CountAsync(a => a.Action == "test.rollback"));
    }

    [Fact]
    public async Task CommitThenFlush_EvictsKeyBumpsGenerationAndDeletesOnlyOwnRows()
    {
        await using var factory = await CreateAsync();
        var otherIds = await SeedRowsAsync(factory, 1, DateTimeOffset.UtcNow.AddMinutes(-1));
        var familyId = Guid.NewGuid();
        var key = $"session:{familyId}";
        await Redis(factory).StringSetAsync(key, "cached");

        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var uow = sp.GetRequiredService<IUnitOfWork>();
            var invalidator = sp.GetRequiredService<ICacheInvalidator>();
            await using (var tx = await uow.BeginTransactionAsync())
            {
                invalidator.InvalidateSession(familyId);
                await uow.SaveChangesAsync();
                await tx.CommitAsync();
            }
            await invalidator.FlushAsync();
        }

        Assert.False(await Redis(factory).KeyExistsAsync(key));
        Assert.Equal("1", (string?)await Redis(factory).StringGetAsync($"{key}:gen"));
        var ttl = await Redis(factory).KeyTimeToLiveAsync($"{key}:gen");
        Assert.NotNull(ttl);
        Assert.True(ttl > TimeSpan.FromHours(23));
        var rows = await RowsAsync(factory);
        Assert.Equal(otherIds, rows.Select(r => r.Id).ToList()); // flush không đụng dòng của request khác
        Assert.Null(rows[0].ClaimId);
    }

    [Fact]
    public async Task ConcurrentClaims_NeverShareRows()
    {
        await using var factory = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var ids = await SeedRowsAsync(factory, 40, now.AddMinutes(-1));

        async Task<IReadOnlyList<CacheInvalidationWorkItem>> ClaimAsync()
        {
            using var scope = factory.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<ICacheInvalidationStore>()
                .ClaimAsync(null, 15, Guid.NewGuid(), now, Lease, CancellationToken.None);
        }

        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(ClaimAsync)));

        var claimed = results.SelectMany(r => r.Select(i => i.Id)).ToList();
        Assert.Equal(claimed.Count, claimed.Distinct().Count());
        Assert.Equal(40, claimed.Count);
        Assert.True(claimed.ToHashSet().SetEquals(ids));
    }

    [Fact]
    public async Task Claim_SkipsRowsLockedByAnotherTransaction_WithoutBlocking()
    {
        await using var factory = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var ids = await SeedRowsAsync(factory, 2, now.AddMinutes(-1));

        await using var conn = new NpgsqlConnection(factory.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var lockTx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand("SELECT 1 FROM \"CacheInvalidations\" WHERE \"Id\" = @id FOR UPDATE", conn, lockTx))
        {
            cmd.Parameters.AddWithValue("id", ids[0]);
            await cmd.ExecuteNonQueryAsync();
        }

        using var scope = factory.Services.CreateScope();
        var claimTask = scope.ServiceProvider.GetRequiredService<ICacheInvalidationStore>()
            .ClaimAsync(null, 10, Guid.NewGuid(), now, Lease, CancellationToken.None);
        var finished = await Task.WhenAny(claimTask, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(claimTask, finished);
        Assert.Equal([ids[1]], (await claimTask).Select(i => i.Id).ToList());
        await lockTx.RollbackAsync();
    }

    [Fact]
    public async Task ExpiredClaim_IsReclaimed_AndStaleOwnerCannotAckOrFail()
    {
        await using var factory = await CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var ids = await SeedRowsAsync(factory, 1, now.AddMinutes(-1));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        using var scope = factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICacheInvalidationStore>();

        Assert.Single(await store.ClaimAsync(null, 10, first, now, Lease, CancellationToken.None));
        Assert.Empty(await store.ClaimAsync(null, 10, second, now.AddSeconds(10), Lease, CancellationToken.None));
        Assert.Single(await store.ClaimAsync(ids, 10, second, now.AddSeconds(31), Lease, CancellationToken.None));

        Assert.Equal(0, await store.CompleteAsync(ids, first, CancellationToken.None));
        Assert.Equal(0, await store.FailAsync(ids, first, "stale", CancellationToken.None));
        var row = Assert.Single(await RowsAsync(factory));
        Assert.Equal(second, row.ClaimId);
        Assert.Equal(0, row.Attempts);

        Assert.Equal(1, await store.CompleteAsync(ids, second, CancellationToken.None));
        Assert.Empty(await RowsAsync(factory));
    }

    [Fact]
    public async Task RedisFailure_KeepsRowWithSanitizedError_AndLaterSucceeds()
    {
        var evictor = new ToggleEvictor();
        await using var factory = await CreateAsync(s => s.Replace(ServiceDescriptor.Singleton<ICacheKeyEvictor>(evictor)));
        var ids = await SeedRowsAsync(factory, 1, DateTimeOffset.UtcNow.AddMinutes(-1));

        using (var scope = factory.Services.CreateScope())
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>().ProcessPendingAsync(100, CancellationToken.None));

        var row = Assert.Single(await RowsAsync(factory));
        Assert.Equal(ids[0], row.Id);
        Assert.Equal(1, row.Attempts);
        Assert.Null(row.ClaimId);
        Assert.NotNull(row.LastError);
        Assert.DoesNotContain("secret", row.LastError);
        Assert.DoesNotContain("redis-host", row.LastError);

        evictor.Fail = false;
        using (var scope = factory.Services.CreateScope())
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>().ProcessPendingAsync(100, CancellationToken.None));
        Assert.Empty(await RowsAsync(factory));
    }

    [Fact]
    public async Task FlushAfterCommit_RedisDown_DoesNotThrowAndKeepsRow()
    {
        var evictor = new ToggleEvictor();
        await using var factory = await CreateAsync(s => s.Replace(ServiceDescriptor.Singleton<ICacheKeyEvictor>(evictor)));

        using (var scope = factory.Services.CreateScope())
        {
            var sp = scope.ServiceProvider;
            var invalidator = sp.GetRequiredService<ICacheInvalidator>();
            invalidator.InvalidatePermissions(Guid.NewGuid());
            await sp.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            await invalidator.FlushAsync();
        }

        var row = Assert.Single(await RowsAsync(factory));
        Assert.Equal(1, row.Attempts);
    }

    private sealed class ToggleEvictor : ICacheKeyEvictor
    {
        public volatile bool Fail = true;

        public Task EvictAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
            => Fail
                ? throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "redis-host:6379,password=secret unreachable")
                : Task.CompletedTask;
    }
}

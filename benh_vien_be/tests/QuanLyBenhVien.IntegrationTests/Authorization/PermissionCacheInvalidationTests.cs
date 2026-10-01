using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Persistence;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Authorization;

/// Chứng minh bằng hành vi (không chỉ DEL key): writer đã đọc thế hệ cũ không được ghi lại quyền cũ sau khi
/// quyền đổi và invalidation chạy xen giữa lúc nó đọc DB và lúc nó ghi cache.
[Collection(IntegrationCollection.Name)]
public class PermissionCacheInvalidationTests
{
    private readonly ContainersFixture _containers;

    public PermissionCacheInvalidationTests(ContainersFixture containers) => _containers = containers;

    /// Sau khi đọc DB (kết quả cũ) chạy hook một lần, rồi mới trả kết quả cho PermissionService.
    private sealed class InterleavingReadService(IUserAccessReadService inner, InterleavingHook hook) : IUserAccessReadService
    {
        public async Task<UserAccessDto?> GetAsync(Guid userId, CancellationToken ct)
        {
            var result = await inner.GetAsync(userId, ct);
            await hook.RunOnceAsync();
            return result;
        }
    }

    private sealed class InterleavingHook
    {
        public Func<Task>? Action { get; set; }

        public async Task RunOnceAsync()
        {
            var action = Action;
            Action = null;
            if (action is not null) await action();
        }
    }

    [Fact]
    public async Task StaleWriter_CannotRepopulateCache_AndNextRequestSeesNewPermissions()
    {
        var hook = new InterleavingHook();
        await using var factory = await ApiFactory.CreateAsync(_containers, configureServices: services =>
        {
            TestServices.RemoveCacheInvalidationWorker(services);
            var original = services.Single(d => d.ServiceType == typeof(IUserAccessReadService));
            services.Remove(original);
            services.AddSingleton(hook);
            services.AddScoped<IUserAccessReadService>(sp => new InterleavingReadService(
                (IUserAccessReadService)ActivatorUtilities.CreateInstance(sp, original.ImplementationType!),
                sp.GetRequiredService<InterleavingHook>()));
        });
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

        // Giữa lúc writer cũ đọc DB và lúc nó ghi cache: gỡ vai trò + invalidation + flush.
        hook.Action = async () =>
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Set<UserRole>().Where(r => r.UserId == userId).ExecuteDeleteAsync();
            var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
            invalidator.InvalidatePermissions(userId);
            await db.SaveChangesAsync();
            await invalidator.FlushAsync();
        };

        UserAccessDto? stale;
        await using (var scope = factory.Services.CreateAsyncScope())
            stale = await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId, CancellationToken.None);

        Assert.Contains(Permissions.Users.Read, stale!.Permissions);                // writer cũ vẫn trả kết quả đã đọc
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Permissions(userId)));     // nhưng không ghi được cache

        await using var next = factory.Services.CreateAsyncScope();
        var fresh = await next.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId, CancellationToken.None);
        Assert.DoesNotContain(Permissions.Users.Read, fresh!.Permissions);
        Assert.True(await redis.KeyExistsAsync(CacheKeys.Permissions(userId)));      // request mới cache được quyền mới
    }

    [Fact]
    public async Task PerRequestCache_ReturnsSameResultWithinScope_ButNewScopeSeesChange()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers, configureServices: TestServices.RemoveCacheInvalidationWorker);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);

        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IPermissionService>();
        var first = await service.GetAsync(userId, CancellationToken.None);
        await TestData.QueryAsync(factory, async db =>
        {
            await db.Set<UserRole>().Where(r => r.UserId == userId).ExecuteDeleteAsync();
            return 0;
        });
        await factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyDeleteAsync(CacheKeys.Permissions(userId));

        Assert.Same(first, await service.GetAsync(userId, CancellationToken.None));
        await using var other = factory.Services.CreateAsyncScope();
        var fresh = await other.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId, CancellationToken.None);
        Assert.DoesNotContain(Permissions.Users.Read, fresh!.Permissions);
    }
}

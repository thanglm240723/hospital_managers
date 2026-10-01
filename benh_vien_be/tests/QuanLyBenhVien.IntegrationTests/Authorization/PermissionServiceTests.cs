using System.Text.Json;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.Persistence;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Authorization;

[Collection(IntegrationCollection.Name)]
public class PermissionServiceTests
{
    private readonly ContainersFixture _containers;

    public PermissionServiceTests(ContainersFixture containers) => _containers = containers;

    private static async Task<UserAccessDto?> GetAsync(ApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId, CancellationToken.None);
    }

    [Fact]
    public async Task FirstCall_LoadsFromDbAndCachesWithoutTtl()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);

        var access = await GetAsync(factory, userId);

        Assert.Contains(Permissions.Users.Read, access!.Permissions);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        using var cached = JsonDocument.Parse((string)(await redis.StringGetAsync(CacheKeys.Permissions(userId)))!);
        Assert.Contains(cached.RootElement.GetProperty("permissions").EnumerateArray(), p => p.GetString() == Permissions.Users.Read);
        Assert.Null(await redis.KeyTimeToLiveAsync(CacheKeys.Permissions(userId)));
    }

    [Fact]
    public async Task CachedValue_IsUsedUntilInvalidated()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);
        await GetAsync(factory, userId);   // làm ấm cache

        await TestData.QueryAsync(factory, async db =>
        {
            await db.Set<UserRole>().Where(r => r.UserId == userId).ExecuteDeleteAsync();   // đổi DB "lén", không invalidate
            return 0;
        });
        Assert.Contains(Permissions.Users.Read, (await GetAsync(factory, userId))!.Permissions);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
            invalidator.InvalidatePermissions(userId);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
            await invalidator.FlushAsync();
        }
        Assert.DoesNotContain(Permissions.Users.Read, (await GetAsync(factory, userId))!.Permissions);
    }

    [Fact]
    public async Task RedisDown_FallsBackToDatabase()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);

        Assert.Contains(Permissions.Users.Read, (await GetAsync(factory, userId))!.Permissions);
    }

    [Fact]
    public async Task InactiveUser_HasNoPermissions_UnknownUser_HasNone()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var inactive = await TestData.CreateUserAsync(factory, TestData.NewEmail(), isActive: false, roleCodes: [SystemRoles.Admin]);

        var access = await GetAsync(factory, inactive);
        Assert.NotNull(access);
        Assert.False(access.IsActive);
        Assert.Empty(access.Permissions);
        var unknown = Guid.NewGuid();
        Assert.Null(await GetAsync(factory, unknown));
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Permissions(unknown)));   // không cache user không tồn tại
    }

    [Fact]
    public async Task RolesAndGrants_AreUnionedWithoutDuplicates_RevokeKeepsRolePermission()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);
        await TestData.GrantAsync(factory, userId, Permissions.Users.Read, Permissions.Catalog.Read);

        var access = (await GetAsync(factory, userId))!;
        Assert.Equal(access.Permissions.Count, access.Permissions.Distinct().Count());
        Assert.Contains(Permissions.Users.Read, access.Permissions);

        await TestData.QueryAsync(factory, async db =>
        {
            var user = await db.Users.Include(u => u.PermissionGrants).SingleAsync(u => u.Id == userId);
            user.RevokePermission(Permissions.Users.Read);
            await db.SaveChangesAsync();
            return 0;
        });
        // cache còn giá trị cũ: xoá để buộc đọc lại DB
        await factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase().KeyDeleteAsync(CacheKeys.Permissions(userId));

        var after = (await GetAsync(factory, userId))!;
        Assert.Contains(Permissions.Users.Read, after.Permissions);   // vẫn còn nhờ role
    }

    [Fact]
    public async Task CorruptCachePayload_IsTreatedAsMiss_AndNeverGrantsEverything()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail());
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await redis.StringSetAsync(CacheKeys.Permissions(userId), "{not json");

        var access = (await GetAsync(factory, userId))!;

        Assert.Empty(access.Permissions);
        Assert.True(access.IsActive);
    }

    [Fact]
    public async Task CachedPayload_CarriesIsActiveAndMustChangePassword()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), mustChangePassword: true);

        var access = (await GetAsync(factory, userId))!;
        Assert.True(access.MustChangePassword);

        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        using var cached = JsonDocument.Parse((string)(await redis.StringGetAsync(CacheKeys.Permissions(userId)))!);
        Assert.True(cached.RootElement.GetProperty("mustChangePassword").GetBoolean());
        Assert.True(cached.RootElement.GetProperty("isActive").GetBoolean());
        var again = (await GetAsync(factory, userId))!;   // đọc từ cache
        Assert.True(again.MustChangePassword);
        Assert.True(again.IsActive);
    }

    [Fact]
    public async Task GrantedPermission_IsPartOfEffectiveSet()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail());
        await TestData.GrantAsync(factory, userId, Permissions.Catalog.Read);

        Assert.Equal(new[] { Permissions.Catalog.Read }, (await GetAsync(factory, userId))!.Permissions);
    }
}

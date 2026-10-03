using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.Persistence;
using QuanLyBenhVien.Persistence.Caching;
using StackExchange.Redis;
using QuanLyBenhVien.Persistence.Seed;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class SeedTests
{
    private readonly ContainersFixture _containers;

    public SeedTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task Startup_SeedsCatalogSystemRolesAndFirstAdmin()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(Permissions.All.Count, await db.Permissions.CountAsync());

        var roles = await db.Roles.Include(r => r.GrantedPermissions).ToListAsync();
        Assert.Equal(SystemRoles.All.Select(r => r.Code).OrderBy(c => c), roles.Select(r => r.Code).OrderBy(c => c));
        Assert.All(roles, r => Assert.True(r.IsSystem));

        var adminRole = roles.Single(r => r.Code == SystemRoles.Admin);
        Assert.Equal(
            Permissions.All.Select(p => p.Code).OrderBy(c => c),
            adminRole.GrantedPermissions.Select(p => p.PermissionCode).OrderBy(c => c));

        var admin = await db.Users.Include(u => u.RoleAssignments).SingleAsync(u => u.Email == factory.AdminEmail);
        Assert.True(admin.MustChangePassword);
        Assert.True(admin.HasRole(adminRole.Id));
    }

    private static async Task<UserAccessDto?> AccessAsync(ApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId, CancellationToken.None);
    }

    private static async Task RunSeederAsync(ApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();
    }

    [Fact]
    public async Task Seeder_AdminPermissionSetExtended_InvalidatesCachedPermissionsOfRoleHolders()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers, configureServices: TestServices.RemoveCacheInvalidationWorker);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

        // Mô phỏng catalog cũ: admin chưa có quyền `extended`, cache đã được làm ấm với tập quyền cũ.
        var extended = Permissions.IdentityAccess.Select(p => p.Code).Last();
        await TestData.QueryAsync(factory, async db =>
        {
            await db.Set<RolePermission>().Where(rp => rp.PermissionCode == extended).ExecuteDeleteAsync();
            return 0;
        });
        await redis.KeyDeleteAsync(CacheKeys.Permissions(userId));
        Assert.DoesNotContain(extended, (await AccessAsync(factory, userId))!.Permissions);
        Assert.True(await redis.KeyExistsAsync(CacheKeys.Permissions(userId)));

        await RunSeederAsync(factory);

        Assert.Contains(extended, (await AccessAsync(factory, userId))!.Permissions);   // request kế tiếp thấy quyền mới
    }

    [Fact]
    public async Task Seeder_AdminPermissionSetUnchanged_LeavesPermissionCacheUntouched()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers, configureServices: TestServices.RemoveCacheInvalidationWorker);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await AccessAsync(factory, userId);
        Assert.True(await redis.KeyExistsAsync(CacheKeys.Permissions(userId)));

        await RunSeederAsync(factory);

        Assert.True(await redis.KeyExistsAsync(CacheKeys.Permissions(userId)));
        Assert.Equal(0, await TestData.QueryAsync(factory, db => db.Set<CacheInvalidation>().CountAsync()));
    }

    [Fact]
    public async Task Seeder_RunTwice_IsIdempotent()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await scope.ServiceProvider.GetRequiredService<IdentitySeeder>().SeedAsync();

        Assert.Equal(SystemRoles.All.Count, await db.Roles.CountAsync());
        Assert.Equal(Permissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(1, await db.Users.CountAsync());
    }
}

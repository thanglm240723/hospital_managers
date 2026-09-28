using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.Persistence;
using QuanLyBenhVien.Infrastructure.Persistence.Seed;
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
            Permissions.IdentityAccess.Select(p => p.Code).OrderBy(c => c),
            adminRole.GrantedPermissions.Select(p => p.PermissionCode).OrderBy(c => c));

        var admin = await db.Users.Include(u => u.RoleAssignments).SingleAsync(u => u.Email == factory.AdminEmail);
        Assert.True(admin.MustChangePassword);
        Assert.True(admin.HasRole(adminRole.Id));
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

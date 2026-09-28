using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence.Repositories.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class RepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly ContainersFixture _containers;

    public RepositoryTests(ContainersFixture containers) => _containers = containers;

    private static User NewUser() => User.Create("A", $"u-{Guid.NewGuid():N}@test.local", "hash", null);

    private async Task<(string Cs, Role Role, User Active, User Inactive, User Other)> SeedAsync()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var role = Role.Create("tester", "Tester");
        var active = NewUser();
        var inactive = NewUser();
        var other = NewUser();
        active.SetRoles([role.Id], null, Now);
        inactive.SetRoles([role.Id], null, Now);
        inactive.Deactivate();

        await using var db = TestDb.Create(cs);
        db.Roles.Add(role);
        db.Users.AddRange(active, inactive, other);
        await db.SaveChangesAsync();
        return (cs, role, active, inactive, other);
    }

    [Fact]
    public async Task CountActiveUsersInRole_IgnoresInactiveAndExcludedUser()
    {
        var (cs, role, active, _, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);
        var repo = new UserRepository(db);

        Assert.Equal(1, await repo.CountActiveUsersInRoleAsync(role.Id, excludingUserId: null));
        Assert.Equal(0, await repo.CountActiveUsersInRoleAsync(role.Id, excludingUserId: active.Id));
    }

    [Fact]
    public async Task GetUserIdsInRole_ReturnsAllAssignedUsers()
    {
        var (cs, role, active, inactive, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);

        var ids = await new UserRepository(db).GetUserIdsInRoleAsync(role.Id);

        Assert.Equal(new[] { active.Id, inactive.Id }.OrderBy(i => i), ids.OrderBy(i => i));
    }

    [Fact]
    public async Task GetWithAccess_LoadsRoleAssignments()
    {
        var (cs, role, active, _, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);

        var user = await new UserRepository(db).GetWithAccessAsync(active.Id);

        Assert.Equal(role.Id, Assert.Single(user!.RoleAssignments).RoleId);
    }

    [Fact]
    public async Task RoleRepository_FindsByCodeAndIdsAndChecksExistence()
    {
        var (cs, role, _, _, _) = await SeedAsync();
        await using var db = TestDb.Create(cs);
        var repo = new RoleRepository(db);

        Assert.Equal(role.Id, (await repo.GetByCodeAsync("tester"))!.Id);
        Assert.Single(await repo.GetByIdsAsync([role.Id, Guid.NewGuid()]));
        Assert.True(await repo.CodeExistsAsync("tester"));
        Assert.False(await repo.CodeExistsAsync("nope"));
    }
}

using QuanLyBenhVien.Domain.Catalog.Facilities;
using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Staff;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Persistence.Repositories.Identity;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class StaffProfilePersistenceTests
{
    private readonly ContainersFixture _containers;

    public StaffProfilePersistenceTests(ContainersFixture containers) => _containers = containers;

    private static User NewUser() => User.Create("A", $"u-{Guid.NewGuid():N}@test.local", "hash", null);

    private static async Task<(Branch, Department, Department)> SeedFacilitiesAsync(string cs)
    {
        var b = Branch.Create("CS1", "Cơ sở 1");
        var d1 = Department.Create(b.Id, "NOI", "Nội", DepartmentKind.Clinical);
        var d2 = Department.Create(b.Id, "NGOAI", "Ngoại", DepartmentKind.Clinical);
        await using var db = TestDb.Create(cs);
        db.Branches.Add(b);
        db.Departments.AddRange(d1, d2);
        await db.SaveChangesAsync();
        return (b, d1, d2);
    }

    [Fact]
    public async Task SaveAndReload_WithTwoScopes()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var (b, d1, d2) = await SeedFacilitiesAsync(cs);
        var user = NewUser();
        var profile = StaffProfile.Create(user.Id, "BS-001");
        profile.SetWorkScopes([(d1.Id, b.Id), (d2.Id, b.Id)]);
        await using (var db = TestDb.Create(cs))
        {
            db.Users.Add(user);
            await new StaffProfileRepository(db).AddAsync(profile);
            await db.SaveChangesAsync();
        }

        await using var read = TestDb.Create(cs);
        var loaded = (await new StaffProfileRepository(read).GetByUserIdAsync(user.Id))!;
        Assert.Equal("BS-001", loaded.StaffCode);
        Assert.Equal(2, loaded.WorkScopes.Count);
    }

    [Fact]
    public async Task DuplicateStaffCode_Throws()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var u1 = NewUser();
        var u2 = NewUser();
        await using var db = TestDb.Create(cs);
        db.Users.AddRange(u1, u2);
        db.StaffProfiles.Add(StaffProfile.Create(u1.Id, "BS-001"));
        await db.SaveChangesAsync();
        db.StaffProfiles.Add(StaffProfile.Create(u2.Id, "BS-001"));
        var ex = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => db.SaveChangesAsync());
        Assert.Equal("IX_StaffProfiles_StaffCode", ex.ConstraintName);
    }

    [Fact]
    public async Task DuplicateUserId_Throws()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var u = NewUser();
        await using var db = TestDb.Create(cs);
        db.Users.Add(u);
        db.StaffProfiles.Add(StaffProfile.Create(u.Id, "BS-001"));
        await db.SaveChangesAsync();
        db.StaffProfiles.Add(StaffProfile.Create(u.Id, "BS-002"));
        var ex = await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => db.SaveChangesAsync());
        Assert.Equal("IX_StaffProfiles_UserId", ex.ConstraintName);
    }

    [Fact]
    public async Task ScopeOnlyChange_WithMarkChanged_BumpsRowVersion()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var (b, d1, d2) = await SeedFacilitiesAsync(cs);
        var user = NewUser();
        var profile = StaffProfile.Create(user.Id, "BS-001");
        profile.SetWorkScopes([(d1.Id, b.Id)]);
        await using (var db = TestDb.Create(cs))
        {
            db.Users.Add(user);
            db.StaffProfiles.Add(profile);
            await db.SaveChangesAsync();
        }

        uint before, after;
        await using (var db = TestDb.Create(cs))
        {
            var repo = new StaffProfileRepository(db);
            var p = (await repo.GetByUserIdAsync(user.Id))!;
            before = p.RowVersion;
            Assert.True(p.SetWorkScopes([(d2.Id, b.Id)]));
            repo.MarkChanged(p);
            await db.SaveChangesAsync();
            after = p.RowVersion;
        }
        Assert.NotEqual(before, after);
        await using var read = TestDb.Create(cs);
        var again = (await new StaffProfileRepository(read).GetByUserIdAsync(user.Id))!;
        Assert.Equal(d2.Id, Assert.Single(again.WorkScopes).DepartmentId);
        Assert.Equal(after, again.RowVersion);
    }
}

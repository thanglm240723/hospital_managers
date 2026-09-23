using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class PersistenceTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly ContainersFixture _containers;

    public PersistenceTests(ContainersFixture containers) => _containers = containers;

    private static string NewEmail() => $"u-{Guid.NewGuid():N}@test.local";

    [Fact]
    public async Task Migration_CreatesIdentityTablesAndDropsLoginHistory()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        await using var db = TestDb.Create(cs);

        var tables = await db.Database
            .SqlQuery<string>($"SELECT table_name AS \"Value\" FROM information_schema.tables WHERE table_schema = 'public'")
            .ToListAsync();

        foreach (var table in new[] { "Users", "Roles", "Permissions", "RolePermissions", "UserRoles", "UserPermissions",
                     "SessionFamilies", "RefreshTokens", "AuditLogs", "AuditRecords", "CacheInvalidations" })
            Assert.Contains(table, tables);
        Assert.DoesNotContain("UserLoginHistories", tables);
    }

    [Fact]
    public async Task UserWithRolesAndGrants_RoundTrips()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var permission = Permission.Create(Permissions.IdentityAccess[0]);
        var role = Role.Create("tester", "Tester");
        role.SetPermissions([permission.Id]);
        var user = User.Create("A", NewEmail(), "hash", null);
        user.SetRoles([role.Id], null, Now);
        user.GrantPermission(permission.Id, "cần", null, Now);

        await using (var db = TestDb.Create(cs))
        {
            db.Permissions.Add(permission);
            db.Roles.Add(role);
            db.Users.Add(user);
            await db.SaveChangesAsync();
        }

        await using (var db = TestDb.Create(cs))
        {
            var loaded = await db.Users.Include(u => u.RoleAssignments).Include(u => u.PermissionGrants)
                .SingleAsync(u => u.Id == user.Id);
            Assert.Equal(role.Id, Assert.Single(loaded.RoleAssignments).RoleId);
            Assert.Equal(permission.Id, Assert.Single(loaded.PermissionGrants).PermissionCode);
            Assert.Single((await db.Roles.Include(r => r.GrantedPermissions).SingleAsync(r => r.Id == role.Id)).GrantedPermissions);
        }
    }

    [Fact]
    public async Task RotatedToken_AddedThroughNavigation_IsInserted()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var user = User.Create("A", NewEmail(), "hash", null);
        var family = SessionFamily.Start(user.Id, "hash-1", Now, null, null);
        await using (var db = TestDb.Create(cs))
        {
            db.Users.Add(user);
            db.SessionFamilies.Add(family);
            await db.SaveChangesAsync();
        }

        await using (var db = TestDb.Create(cs))
        {
            var loaded = await db.SessionFamilies.Include(f => f.Tokens).SingleAsync(f => f.Id == family.Id);
            Assert.Equal(RotationResult.Rotated, loaded.Rotate("hash-1", "hash-2", Now.AddMinutes(1)));
            await db.SaveChangesAsync();   // ném DbUpdateConcurrencyException nếu thiếu ValueGeneratedNever
        }

        await using (var db = TestDb.Create(cs))
            Assert.Equal(2, await db.RefreshTokens.CountAsync(t => t.FamilyId == family.Id));
    }

    [Fact]
    public async Task RefreshTokenHash_IsUnique()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var user = User.Create("A", NewEmail(), "hash", null);
        await using var db = TestDb.Create(cs);
        db.Users.Add(user);
        db.SessionFamilies.Add(SessionFamily.Start(user.Id, "same-hash", Now, null, null));
        db.SessionFamilies.Add(SessionFamily.Start(user.Id, "same-hash", Now, null, null));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Transaction_DisposedWithoutCommit_RollsBack()
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var email = NewEmail();

        await using (var db = TestDb.Create(cs))
        await using (await db.BeginTransactionAsync())
        {
            db.Users.Add(User.Create("A", email, "hash", null));
            await db.SaveChangesAsync();
        }

        await using (var db = TestDb.Create(cs))
            Assert.False(await db.Users.AnyAsync(u => u.Email == email));
    }
}

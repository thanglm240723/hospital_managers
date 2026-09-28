using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence;

public class EntityConfigurationTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public void UserConfiguration_SetsMaxLengthsAndIgnoresDomainEvents()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(User))!;

        Assert.Equal(256, entityType.FindProperty(nameof(User.Email))!.GetMaxLength());
        Assert.Equal(200, entityType.FindProperty(nameof(User.FullName))!.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(User.PasswordHash))!.GetMaxLength());
        Assert.NotNull(entityType.FindNavigation(nameof(User.RoleAssignments)));
        Assert.Null(entityType.FindProperty(nameof(User.DomainEvents)));
    }

    [Fact]
    public async Task UserConfiguration_EnforcesUniqueEmailIndex()
    {
        // The EF Core InMemory provider does not enforce unique indexes (by design —
        // see dotnet/efcore#3850), so a real constraint check needs a provider that does.
        // SQLite's in-memory mode enforces the unique index configured in UserConfiguration.
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options);
        context.Database.EnsureCreated();

        context.Users.Add(User.Create("A", "dup@example.com", "hash1", null));
        await context.SaveChangesAsync();

        context.Users.Add(User.Create("B", "dup@example.com", "hash2", null));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void AuditLogConfiguration_SetsMaxLengthsAndIndexes()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AuditLog))!;

        Assert.Equal(100, entityType.FindProperty(nameof(AuditLog.EntityName))!.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(AuditLog.EntityId))!.GetMaxLength());
        Assert.Contains(entityType.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
                { nameof(AuditLog.EntityName), nameof(AuditLog.EntityId), nameof(AuditLog.ChangedAt) }));
        Assert.Contains(entityType.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
                { nameof(AuditLog.ChangedByUserId), nameof(AuditLog.ChangedAt) }));
    }
}

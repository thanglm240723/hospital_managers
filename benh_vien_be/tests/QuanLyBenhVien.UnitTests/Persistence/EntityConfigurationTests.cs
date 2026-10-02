using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence;
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
    public void UserConfiguration_DeclaresUniqueEmailIndexAndXminRowVersion()
    {
        // Không dùng SQLite: cột hệ thống xmin của PostgreSQL không có ở đó. Ràng buộc thật được chứng minh ở integration test.
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(User))!;

        var index = Assert.Single(entityType.GetIndexes(), i => i.Properties.Single().Name == nameof(User.Email));
        Assert.True(index.IsUnique);
        Assert.True(entityType.FindProperty(nameof(User.RowVersion))!.IsConcurrencyToken);
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

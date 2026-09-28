using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence.Interceptors;

sealed class FakeCurrentUser : ICurrentUser
{
    public required Guid? UserId { get; init; }
    public Guid? SessionFamilyId => null;
}

sealed class FakeRequestContext : IRequestContext
{
    public string? CorrelationId { get; init; }
    public string? IpAddress => null;
    public string? UserAgent => null;
}

sealed class ProbeLink : IAuditable
{
    public Guid LeftId { get; set; }
    public string RightCode { get; set; } = "";
}

sealed class ProbeDbContext : DbContext
{
    public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<ProbeLink> Links => Set<ProbeLink>();

    // Real UserConfiguration (Task 6) does this too — without it, EF can't map
    // User.DomainEvents (an AggregateRoot computed property) and model building throws.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().Ignore(u => u.DomainEvents);
        modelBuilder.Entity<UserRole>().HasKey(r => new { r.UserId, r.RoleId });
        modelBuilder.Entity<UserPermission>().HasKey(p => new { p.UserId, p.PermissionCode });
        modelBuilder.Entity<ProbeLink>().HasKey(l => new { l.LeftId, l.RightCode });
    }
}

public class AuditSaveChangesInterceptorTests
{
    private static ProbeDbContext CreateContext(Guid? currentUserId)
    {
        var interceptor = new AuditSaveChangesInterceptor(
            new FakeCurrentUser { UserId = currentUserId }, new FakeRequestContext { CorrelationId = "corr-test" });
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new ProbeDbContext(options);
    }

    [Fact]
    public async Task SaveChanges_OnAddedAuditableEntity_WritesCreatedAuditLog()
    {
        using var context = CreateContext(Guid.NewGuid());
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var log = Assert.Single(context.AuditLogs);
        Assert.Equal(nameof(User), log.EntityName);
        Assert.Equal(user.Id.ToString(), log.EntityId);
        Assert.Equal(AuditAction.Created, log.Action);
        Assert.DoesNotContain("PasswordHash", log.Changes);
    }

    [Fact]
    public async Task SaveChanges_OnModifiedAuditableEntity_WritesUpdatedAuditLogWithOnlyChangedFields()
    {
        using var context = CreateContext(Guid.NewGuid());
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.UpdateProfile("Nguyen Van B", null);
        await context.SaveChangesAsync();

        var updateLog = Assert.Single(context.AuditLogs, l => l.Action == AuditAction.Updated);
        Assert.Contains("FullName", updateLog.Changes);
        Assert.DoesNotContain("PasswordHash", updateLog.Changes);
    }

    [Fact]
    public async Task SaveChanges_OnDeletedAuditableEntity_WritesDeletedAuditLog()
    {
        using var context = CreateContext(Guid.NewGuid());
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        var deleteLog = Assert.Single(context.AuditLogs, l => l.Action == AuditAction.Deleted);
        Assert.Equal(user.Id.ToString(), deleteLog.EntityId);
    }

    [Fact]
    public async Task SaveChanges_NoCurrentUser_RecordsNullChangedBy()
    {
        using var context = CreateContext(currentUserId: null);
        var user = User.Create("Seed Admin", "seed@example.com", "hash", null);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var log = Assert.Single(context.AuditLogs);
        Assert.Null(log.ChangedByUserId);
    }

    [Fact]
    public async Task SaveChanges_CompositeKeyEntity_UsesJoinedKeyAsEntityId()
    {
        using var context = CreateContext(Guid.NewGuid());
        var leftId = Guid.NewGuid();

        context.Links.Add(new ProbeLink { LeftId = leftId, RightCode = "users.read" });
        await context.SaveChangesAsync();

        var log = Assert.Single(context.AuditLogs);
        Assert.Equal(nameof(ProbeLink), log.EntityName);
        Assert.Equal($"{leftId},users.read", log.EntityId);
    }

    [Fact]
    public async Task SaveChanges_RecordsCorrelationId()
    {
        using var context = CreateContext(Guid.NewGuid());

        context.Users.Add(User.Create("A", "corr@example.com", "hash", null));
        await context.SaveChangesAsync();

        Assert.Equal("corr-test", Assert.Single(context.AuditLogs).CorrelationId);
    }
}

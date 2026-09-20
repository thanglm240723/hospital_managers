using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Constants;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Persistence.Interceptors;

sealed class FakeCurrentUser : ICurrentUser
{
    public required Guid? UserId { get; init; }
    public string? Email => null;
    public string? Role => null;
    public bool IsAuthenticated => UserId is not null;
}

sealed class ProbeDbContext : DbContext
{
    public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Real UserConfiguration (Task 6) does this too — without it, EF can't map
    // User.DomainEvents (an AggregateRoot computed property) and model building throws.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<User>().Ignore(u => u.DomainEvents);
}

public class AuditSaveChangesInterceptorTests
{
    private static ProbeDbContext CreateContext(Guid? currentUserId)
    {
        var interceptor = new AuditSaveChangesInterceptor(new FakeCurrentUser { UserId = currentUserId });
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
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);

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
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);
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
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);
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
        var user = User.Create("Seed Admin", "seed@example.com", "hash", null, Roles.Admin);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var log = Assert.Single(context.AuditLogs);
        Assert.Null(log.ChangedByUserId);
    }
}

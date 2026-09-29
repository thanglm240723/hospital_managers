using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Persistence;
using QuanLyBenhVien.Persistence.Repositories.Common;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence.Repositories.Common;

file sealed class StubCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public Guid? SessionFamilyId => null;
    public int? SecurityVersion => null;
}

file sealed class StubRequestContext : IRequestContext
{
    public string? CorrelationId => "corr-1";
    public string? IpAddress => "10.0.0.9";
    public string? UserAgent => "UA";
}

public class AuditWriterTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    private static (AppDbContext Db, AuditWriter Writer) Create(Guid? currentUserId)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var time = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now);
        return (db, new AuditWriter(db, new StubCurrentUser { UserId = currentUserId }, new StubRequestContext(), time));
    }

    [Fact]
    public async Task Record_FillsRequestContextAndDefaultsActorToCurrentUser()
    {
        var current = Guid.NewGuid();
        var (db, writer) = Create(current);

        writer.Record("auth.logout", AuditResult.Succeeded, resourceType: "SessionFamily", resourceId: "f-1");
        await db.SaveChangesAsync();

        var record = Assert.Single(db.AuditRecords);
        Assert.Equal(current, record.ActorId);
        Assert.Equal("corr-1", record.CorrelationId);
        Assert.Equal("10.0.0.9", record.IpAddress);
        Assert.Equal(Now, record.TimestampUtc);
    }

    [Fact]
    public async Task Record_ExplicitActorAndMetadata()
    {
        var actor = Guid.NewGuid();
        var (db, writer) = Create(currentUserId: null);

        writer.Record("auth.login", AuditResult.Failed, "InvalidPassword", actorId: actor,
            metadata: new Dictionary<string, object?> { ["count"] = 2 });
        await db.SaveChangesAsync();

        var record = Assert.Single(db.AuditRecords);
        Assert.Equal(actor, record.ActorId);
        Assert.Equal("{\"count\":2}", record.Metadata);
    }

    [Fact]
    public void Record_OversizedMetadata_Throws()
    {
        var (_, writer) = Create(null);

        Assert.Throws<ArgumentException>(() => writer.Record("x.y", AuditResult.Succeeded,
            metadata: new Dictionary<string, object?> { ["blob"] = new string('a', 5000) }));
    }
}

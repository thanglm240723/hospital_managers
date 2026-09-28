using QuanLyBenhVien.Application.Common.Interfaces;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Infrastructure.Auditing;
using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Infrastructure.Auditing;

sealed class StubCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public Guid? SessionFamilyId => null;
    public bool IsAuthenticated => UserId is not null;
}

sealed class StubRequestContext : IRequestContext
{
    public string? CorrelationId => "corr-1";
    public string? IpAddress => "10.0.0.9";
    public string? UserAgent => "UA";
}

sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}

public class AuditRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    private static (AppDbContext Db, AuditRecorder Recorder) Create(Guid? currentUserId)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new AuditRecorder(db, new StubCurrentUser { UserId = currentUserId }, new StubRequestContext(), new FixedTimeProvider(Now)));
    }

    [Fact]
    public async Task Record_FillsRequestContextAndDefaultsActorToCurrentUser()
    {
        var current = Guid.NewGuid();
        var (db, recorder) = Create(current);

        recorder.Record("auth.logout", AuditResult.Succeeded, resourceType: "SessionFamily", resourceId: "f-1");
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
        var (db, recorder) = Create(currentUserId: null);

        recorder.Record("auth.login", AuditResult.Failed, "InvalidPassword", actorId: actor,
            metadata: new Dictionary<string, object?> { ["count"] = 2 });
        await db.SaveChangesAsync();

        var record = Assert.Single(db.AuditRecords);
        Assert.Equal(actor, record.ActorId);
        Assert.Equal("{\"count\":2}", record.Metadata);
    }

    [Fact]
    public void Record_OversizedMetadata_Throws()
    {
        var (_, recorder) = Create(null);

        Assert.Throws<ArgumentException>(() => recorder.Record("x.y", AuditResult.Succeeded,
            metadata: new Dictionary<string, object?> { ["blob"] = new string('a', 5000) }));
    }
}

using QuanLyBenhVien.Domain.Common.Auditing;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Domain.Common.Auditing;

public class AuditRecordTests
{
    [Fact]
    public void Create_SetsAllFields()
    {
        var actor = Guid.NewGuid();
        var at = new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

        var record = AuditRecord.Create(actor, "auth.login", AuditResult.Failed, "InvalidPassword", "User", "u-1",
            "corr-1", "10.0.0.1", "UA", "{\"k\":1}", at);

        Assert.NotEqual(Guid.Empty, record.Id);
        Assert.Equal(actor, record.ActorId);
        Assert.Equal("auth.login", record.Action);
        Assert.Equal(AuditResult.Failed, record.Result);
        Assert.Equal("InvalidPassword", record.Reason);
        Assert.Equal("corr-1", record.CorrelationId);
        Assert.Equal(at, record.TimestampUtc);
    }

    [Fact]
    public void Create_EmptyAction_Throws()
        => Assert.Throws<ArgumentException>(() =>
            AuditRecord.Create(null, " ", AuditResult.Succeeded, null, null, null, null, null, null, null, DateTimeOffset.UtcNow));

    [Fact]
    public void AuditRecord_IsNotAuditable()
        => Assert.False(typeof(IAuditable).IsAssignableFrom(typeof(AuditRecord)));
}

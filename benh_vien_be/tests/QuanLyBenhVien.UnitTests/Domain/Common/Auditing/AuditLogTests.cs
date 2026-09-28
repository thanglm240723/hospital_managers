using QuanLyBenhVien.Domain.Common.Auditing;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Domain.Common.Auditing;

public class AuditLogTests
{
    [Fact]
    public void Create_SetsAllFieldsAndGeneratesId()
    {
        var changedBy = Guid.NewGuid();

        var log = AuditLog.Create("User", "abc-123", AuditAction.Updated, changedBy,
            "{\"FullName\":{\"Old\":\"A\",\"New\":\"B\"}}");

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal("User", log.EntityName);
        Assert.Equal("abc-123", log.EntityId);
        Assert.Equal(AuditAction.Updated, log.Action);
        Assert.Equal(changedBy, log.ChangedByUserId);
        Assert.Equal("{\"FullName\":{\"Old\":\"A\",\"New\":\"B\"}}", log.Changes);
    }

    [Fact]
    public void Create_WithNullChangedBy_AllowsSystemActions()
    {
        var log = AuditLog.Create("User", "abc-123", AuditAction.Created, null, "{}");

        Assert.Null(log.ChangedByUserId);
    }

    [Fact]
    public void AuditLog_DoesNotImplementIAuditable()
    {
        Assert.False(typeof(IAuditable).IsAssignableFrom(typeof(AuditLog)));
    }

    [Fact]
    public void Create_StoresCorrelationId()
    {
        var log = AuditLog.Create("User", "abc-123", AuditAction.Updated, null, "{}", "corr-9");

        Assert.Equal("corr-9", log.CorrelationId);
    }
}

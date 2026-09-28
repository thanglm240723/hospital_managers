namespace QuanLyBenhVien.Domain.Common.Auditing;

/// Audit truy cập/bảo mật (Đặc tả kỹ thuật §3.3). Chỉ insert. KHÔNG implement IAuditable.
public sealed class AuditRecord : Entity<Guid>
{
    public Guid? ActorId { get; private set; }
    public string Action { get; private set; } = default!;
    public string? ResourceType { get; private set; }
    public string? ResourceId { get; private set; }
    public AuditResult Result { get; private set; }
    public string? Reason { get; private set; }
    public string? CorrelationId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset TimestampUtc { get; private set; }
    public string? Metadata { get; private set; }

    private AuditRecord() { }

    public static AuditRecord Create(Guid? actorId, string action, AuditResult result, string? reason,
        string? resourceType, string? resourceId, string? correlationId, string? ipAddress, string? userAgent,
        string? metadataJson, DateTimeOffset timestampUtc)
    {
        if (string.IsNullOrWhiteSpace(action))
            throw new ArgumentException("Action cannot be empty.", nameof(action));

        return new AuditRecord
        {
            Id = Guid.CreateVersion7(),
            ActorId = actorId,
            Action = action,
            Result = result,
            Reason = reason,
            ResourceType = resourceType,
            ResourceId = resourceId,
            CorrelationId = correlationId,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Metadata = metadataJson,
            TimestampUtc = timestampUtc
        };
    }
}

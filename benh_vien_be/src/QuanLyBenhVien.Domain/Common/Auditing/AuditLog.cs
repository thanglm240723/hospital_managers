namespace QuanLyBenhVien.Domain.Common.Auditing;

public sealed class AuditLog : Entity<Guid>
{
    public string EntityName { get; private set; } = default!;
    public string EntityId { get; private set; } = default!;
    public AuditAction Action { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string Changes { get; private set; } = default!;
    public string? CorrelationId { get; private set; }

    private AuditLog() { }

    public static AuditLog Create(string entityName, string entityId, AuditAction action,
        Guid? changedByUserId, string changesJson, string? correlationId = null)
    {
        return new AuditLog
        {
            Id = Guid.CreateVersion7(),
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTimeOffset.UtcNow,
            Changes = changesJson,
            CorrelationId = correlationId
        };
    }
}

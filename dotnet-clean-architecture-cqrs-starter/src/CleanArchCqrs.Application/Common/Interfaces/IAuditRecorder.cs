using CleanArchCqrs.Domain.Common.Auditing;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Ghi AuditRecord vào unit of work hiện tại. Caller tự SaveChangesAsync —
/// nhánh thất bại cần lưu vết (401 login, 429, reuse) phải save TRƯỚC khi ném lỗi.
public interface IAuditRecorder
{
    void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null);
}

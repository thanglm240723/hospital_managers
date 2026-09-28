using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Application.Common.Auditing;

/// Chỉ thêm bản ghi audit vào unit of work đang mở — không tự SaveChanges.
public interface IAuditWriter
{
    void Record(
        string action,
        AuditResult result,
        string? reason = null,
        string? resourceType = null,
        string? resourceId = null,
        Guid? actorId = null,
        IReadOnlyDictionary<string, object?>? metadata = null);
}

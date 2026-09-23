namespace CleanArchCqrs.Application.Common.Interfaces;

/// Đánh dấu query/command trả dữ liệu nhạy cảm (bệnh án, kết quả CLS…). Khai báo tường minh —
/// không suy từ kiểu dữ liệu trả về (Đặc tả kỹ thuật §3.3).
public interface IAuditedRequest
{
    string AuditAction { get; }
    string AuditResourceType { get; }
    string? AuditResourceId { get; }
}

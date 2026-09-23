namespace CleanArchCqrs.Application.Common.Interfaces;

/// Invalidate*: ghi lệnh xoá vào DB cùng transaction nghiệp vụ (không bao giờ thất lạc).
/// FlushAsync: gọi SAU khi commit để xoá Redis ngay; lỗi thì worker làm lại.
public interface ICacheInvalidator
{
    void InvalidateSession(Guid sessionFamilyId);
    void InvalidatePermissions(Guid userId);
    Task FlushAsync(CancellationToken ct = default);
}

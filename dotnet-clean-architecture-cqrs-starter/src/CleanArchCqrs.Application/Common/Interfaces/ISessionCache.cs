using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Ghi session:{familyId} cho Gateway đọc. Chỉ gọi cho family vừa tạo (login).
/// Các trường hợp khác: xoá key qua ICacheInvalidator, Gateway tự nạp lại qua BE.
public interface ISessionCache
{
    Task SetAsync(SessionCacheEntry entry, CancellationToken ct = default);
}

namespace QuanLyBenhVien.Application.Common.Identity;

/// Quyền hiệu lực qua cache Redis (không TTL, xoá bằng CacheInvalidations) và cache theo request.
/// Trả null nếu user không tồn tại.
public interface IPermissionService
{
    Task<UserAccessDto?> GetAsync(Guid userId, CancellationToken ct);
}

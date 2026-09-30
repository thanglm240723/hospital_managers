namespace QuanLyBenhVien.Application.Common.Caching;

/// Xoá cache tin cậy (spec V2 §5.2): Invalidate* chỉ thêm dòng `CacheInvalidations` vào unit of work hiện tại
/// (không tự save) — rollback thì biến mất cùng dữ liệu nghiệp vụ. `FlushAsync` gọi SAU commit, không ném lỗi;
/// thất bại thì dòng ở lại cho worker.
public interface ICacheInvalidator
{
    void InvalidateSession(Guid sessionFamilyId);

    void InvalidatePermissions(Guid userId);

    Task FlushAsync(CancellationToken ct = default);
}

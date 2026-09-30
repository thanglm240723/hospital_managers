namespace QuanLyBenhVien.Application.Common.Caching;

/// Bảng chờ xoá cache (Persistence implement). Claim dùng lease: chỉ chủ claim còn hiệu lực mới ack/fail được.
public interface ICacheInvalidationStore
{
    /// Thêm dòng vào unit of work hiện tại, không SaveChanges.
    Guid Enqueue(string key, DateTimeOffset now);

    /// Transaction ngắn riêng (không được gọi khi đang giữ transaction nghiệp vụ), commit trước khi trả về.
    /// `ids = null`: worker lấy các dòng cũ nhất; `ids` cụ thể: flush của request chỉ lấy dòng mình enqueue.
    Task<IReadOnlyList<CacheInvalidationWorkItem>> ClaimAsync(
        IReadOnlyCollection<Guid>? ids, int take, Guid claimId, DateTimeOffset now, TimeSpan lease, CancellationToken ct);

    /// Xoá các dòng còn thuộc `claimId`; trả số dòng đã xoá.
    Task<int> CompleteAsync(IReadOnlyCollection<Guid> ids, Guid claimId, CancellationToken ct);

    /// Tăng Attempts, lưu lỗi đã làm sạch và nhả claim cho các dòng còn thuộc `claimId`; trả số dòng cập nhật.
    Task<int> FailAsync(IReadOnlyCollection<Guid> ids, Guid claimId, string safeError, CancellationToken ct);
}

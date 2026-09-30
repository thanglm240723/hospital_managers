namespace QuanLyBenhVien.Application.Common.Caching;

/// Infrastructure implement: với mỗi key, xoá key + tăng `{key}:gen` + đặt TTL thế hệ một cách nguyên tử.
/// Idempotent theo nghĩa an toàn khi chạy lại (thế hệ chỉ tăng thêm).
public interface ICacheKeyEvictor
{
    Task EvictAsync(IReadOnlyCollection<string> keys, CancellationToken ct);
}

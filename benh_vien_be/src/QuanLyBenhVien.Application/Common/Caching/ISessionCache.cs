namespace QuanLyBenhVien.Application.Common.Caching;

public interface ISessionCache
{
    /// Trả null khi Redis lỗi (không phân biệt được với "chưa từng ghi").
    Task<CacheGeneration?> ReadGenerationAsync(Guid sessionFamilyId, CancellationToken ct = default);

    /// Ghi có điều kiện: chỉ ghi khi thế hệ hiện tại của key khớp `expected`. Không ném khi Redis lỗi.
    Task SetIfGenerationUnchangedAsync(SessionCacheEntry entry, CacheGeneration expected, CancellationToken ct = default);
}

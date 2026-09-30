using Microsoft.Extensions.Logging;

namespace QuanLyBenhVien.Application.Common.Caching;

/// Điều phối claim → eviction → ack/fail. Không giữ transaction DB khi gọi Redis (claim đã commit).
public sealed class CacheInvalidationProcessor
{
    public static readonly TimeSpan Lease = TimeSpan.FromSeconds(30);

    /// Ngắn hơn lease để không ack sau khi lease đã có thể bị worker khác nhận lại.
    public static readonly TimeSpan EvictionTimeout = TimeSpan.FromSeconds(10);

    private const int AlertAfterAttempts = 10;

    private readonly ICacheInvalidationStore _store;
    private readonly ICacheKeyEvictor _evictor;
    private readonly TimeProvider _time;
    private readonly ILogger<CacheInvalidationProcessor> _logger;

    public CacheInvalidationProcessor(
        ICacheInvalidationStore store, ICacheKeyEvictor evictor, TimeProvider time, ILogger<CacheInvalidationProcessor> logger)
    {
        _store = store;
        _evictor = evictor;
        _time = time;
        _logger = logger;
    }

    /// Worker: xử lý tối đa `batchSize` dòng cũ nhất; trả số dòng đã ack thành công.
    public Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct = default)
        => ProcessAsync(null, batchSize, ct);

    /// Dùng chung cho worker (`ids = null`) và flush của request (chỉ các Id request đã enqueue).
    internal async Task<int> ProcessAsync(IReadOnlyCollection<Guid>? ids, int take, CancellationToken ct)
    {
        var claimId = Guid.CreateVersion7();
        var items = await _store.ClaimAsync(ids, take, claimId, _time.GetUtcNow(), Lease, ct);
        if (items.Count == 0) return 0;

        var claimedIds = items.Select(i => i.Id).ToList();
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(EvictionTimeout);
            await _evictor.EvictAsync(items.Select(i => i.Key).Distinct().ToList(), timeout.Token);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Chỉ lưu tên loại lỗi: message của exception Redis có thể chứa endpoint/cấu hình kết nối.
            var safeError = ex is OperationCanceledException ? "eviction_timeout" : ex.GetType().Name;
            await _store.FailAsync(claimedIds, claimId, safeError, CancellationToken.None);
            var alert = items.Any(i => i.Attempts + 1 >= AlertAfterAttempts);
            _logger.Log(alert ? LogLevel.Error : LogLevel.Warning,
                "Could not evict {Count} cache keys ({Error}); will retry", items.Count, safeError);
            return 0;
        }

        var completed = await _store.CompleteAsync(claimedIds, claimId, CancellationToken.None);
        if (completed < claimedIds.Count)
            _logger.LogWarning("Lost ownership of {Lost} cache invalidation claims; another worker will redo them",
                claimedIds.Count - completed);
        return completed;
    }
}

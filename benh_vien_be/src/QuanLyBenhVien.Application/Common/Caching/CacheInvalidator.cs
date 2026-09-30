using Microsoft.Extensions.Logging;

namespace QuanLyBenhVien.Application.Common.Caching;

/// Scoped theo request: nhớ các Id đã enqueue để FlushAsync chỉ claim đúng các dòng đó.
internal sealed class CacheInvalidator : ICacheInvalidator
{
    private readonly ICacheInvalidationStore _store;
    private readonly CacheInvalidationProcessor _processor;
    private readonly TimeProvider _time;
    private readonly ILogger<CacheInvalidator> _logger;
    private readonly Dictionary<string, Guid> _pending = new(StringComparer.Ordinal);

    public CacheInvalidator(
        ICacheInvalidationStore store, CacheInvalidationProcessor processor, TimeProvider time, ILogger<CacheInvalidator> logger)
    {
        _store = store;
        _processor = processor;
        _time = time;
        _logger = logger;
    }

    public void InvalidateSession(Guid sessionFamilyId) => Enqueue(CacheKeyFormat.Session(sessionFamilyId));

    public void InvalidatePermissions(Guid userId) => Enqueue(CacheKeyFormat.Permissions(userId));

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0) return;
        var ids = _pending.Values.ToList();
        _pending.Clear();
        try
        {
            await _processor.ProcessAsync(ids, ids.Count, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Nghiệp vụ đã commit: không báo lỗi cho request, worker sẽ xử lý dòng còn lại.
            _logger.LogWarning("Deferred {Count} cache invalidations to the background worker ({Error})",
                ids.Count, ex.GetType().Name);
        }
    }

    private void Enqueue(string key)
    {
        if (_pending.ContainsKey(key)) return;
        _pending[key] = _store.Enqueue(key, _time.GetUtcNow());
    }
}

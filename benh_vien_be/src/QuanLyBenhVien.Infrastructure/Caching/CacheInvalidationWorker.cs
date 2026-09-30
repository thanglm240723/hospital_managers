using QuanLyBenhVien.Application.Common.Caching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace QuanLyBenhVien.Infrastructure.Caching;

/// Chạy lúc khởi động và mỗi 5 giây, tối đa 100 dòng mỗi lượt; lỗi không bỏ dòng (processor lưu Attempts/lỗi).
public sealed class CacheInvalidationWorker : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CacheInvalidationWorker> _logger;

    public CacheInvalidationWorker(IServiceScopeFactory scopes, ILogger<CacheInvalidationWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>()
                    .ProcessPendingAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                // Không log exception thô: message có thể chứa cấu hình kết nối.
                _logger.LogError("Cache invalidation worker iteration failed ({Error}); will retry", ex.GetType().Name);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

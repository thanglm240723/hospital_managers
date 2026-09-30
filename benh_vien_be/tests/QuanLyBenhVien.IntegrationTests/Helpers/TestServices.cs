using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using QuanLyBenhVien.Infrastructure.Caching;

namespace QuanLyBenhVien.IntegrationTests.Helpers;

public static class TestServices
{
    /// Tắt worker nền để test tự điều khiển việc claim/xử lý bảng chờ (worker chạy song song sẽ claim mất dòng).
    public static void RemoveCacheInvalidationWorker(IServiceCollection services)
    {
        var worker = services.Where(d => d.ServiceType == typeof(IHostedService)
                && d.ImplementationType == typeof(CacheInvalidationWorker))
            .ToList();
        foreach (var descriptor in worker)
            services.Remove(descriptor);
    }
}

using CleanArchCqrs.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.IntegrationTests.Helpers;

public static class TestServices
{
    /// Tắt worker để test tự điều khiển thời điểm xử lý bảng CacheInvalidations.
    public static void RemoveCacheInvalidationWorker(IServiceCollection services)
        => services.Remove(services.Single(d => d.ImplementationType == typeof(CacheInvalidationWorker)));
}

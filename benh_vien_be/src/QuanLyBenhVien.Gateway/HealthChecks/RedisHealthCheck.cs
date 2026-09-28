using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace QuanLyBenhVien.Gateway.HealthChecks;

/// Bản sao của health check bên API (Gateway không tham chiếu Infrastructure).
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis unreachable", ex);
        }
    }
}

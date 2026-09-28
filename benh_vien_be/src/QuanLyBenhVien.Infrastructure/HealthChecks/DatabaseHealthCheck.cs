using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace QuanLyBenhVien.Infrastructure.HealthChecks;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => await _db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Database unreachable");
}

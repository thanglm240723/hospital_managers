using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Persistence.Authorization;

/// Ghi audit từ chối bằng DbContext riêng nên sống sót khi transaction nghiệp vụ rollback. Không bao giờ ném lại.
internal sealed class DeniedAccessRecorder : IDeniedAccessRecorder
{
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;
    private readonly TimeProvider _time;
    private readonly ILogger<DeniedAccessRecorder> _logger;

    public DeniedAccessRecorder(IDbContextFactory<AppDbContext> factory, ICurrentUser currentUser, IRequestContext requestContext,
        TimeProvider time, ILogger<DeniedAccessRecorder> logger)
    {
        _factory = factory;
        _currentUser = currentUser;
        _requestContext = requestContext;
        _time = time;
        _logger = logger;
    }

    public async Task RecordAsync(string action, ResourceRef resource, string reason, CancellationToken ct)
    {
        var correlationId = _requestContext.CorrelationId;
        try
        {
            // Cố ý không dùng ct: client hủy request không được làm mất audit từ chối.
            await using var db = await _factory.CreateDbContextAsync(CancellationToken.None);
            db.AuditRecords.Add(AuditRecord.Create(_currentUser.UserId, action, AuditResult.Denied, reason,
                resource.ResourceType, resource.Id.ToString(), correlationId, _requestContext.IpAddress,
                _requestContext.UserAgent, null, _time.GetUtcNow()));
            await db.SaveChangesAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record denied access {Action} {ResourceType}. CorrelationId={CorrelationId}",
                action, resource.ResourceType, correlationId);
        }
    }
}

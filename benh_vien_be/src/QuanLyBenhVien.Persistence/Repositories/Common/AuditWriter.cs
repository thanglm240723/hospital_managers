using System.Text;
using System.Text.Json;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Persistence.Repositories.Common;

internal sealed class AuditWriter : IAuditWriter
{
    private const int MaxMetadataBytes = 4096;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _request;
    private readonly TimeProvider _time;

    public AuditWriter(AppDbContext db, ICurrentUser currentUser, IRequestContext request, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _request = request;
        _time = time;
    }

    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);
        if (metadataJson is not null && Encoding.UTF8.GetByteCount(metadataJson) > MaxMetadataBytes)
            throw new ArgumentException($"Audit metadata exceeds {MaxMetadataBytes} bytes.", nameof(metadata));

        _db.AuditRecords.Add(AuditRecord.Create(
            actorId ?? _currentUser.UserId, action, result, reason, resourceType, resourceId,
            _request.CorrelationId, _request.IpAddress, _request.UserAgent, metadataJson, _time.GetUtcNow()));
    }
}

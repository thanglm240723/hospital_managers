using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Logout;

/// Idempotent: cookie thiếu/không hợp lệ/phiên đã chết đều coi như đã logout.
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public LogoutCommandHandler(ISessionRepository sessions, IRefreshTokenGenerator refreshTokens,
        ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken)) return;
        var hash = _refreshTokens.Hash(request.RefreshToken);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var familyId = await _sessions.FindFamilyIdByTokenHashAsync(hash, ct);
        if (familyId is null) return;
        var family = await _sessions.GetForUpdateAsync(familyId.Value, hash, ct);
        if (family is null || family.Status != SessionStatus.Active) return;

        family.Revoke(SessionRevokeReason.Logout, _time.GetUtcNow());
        _audit.Record(AuditActions.Logout, AuditResult.Succeeded,
            resourceType: nameof(SessionFamily), resourceId: family.Id.ToString(), actorId: family.UserId);
        _cacheInvalidator.InvalidateSession(family.Id);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

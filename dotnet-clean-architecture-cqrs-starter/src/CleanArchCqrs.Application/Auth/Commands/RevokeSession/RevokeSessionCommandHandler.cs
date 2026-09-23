using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.RevokeSession;

public sealed class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly ISessionRepository _sessions;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public RevokeSessionCommandHandler(ICurrentUser currentUser, ISessionRepository sessions, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _sessions = sessions;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var family = await _sessions.GetForUpdateAsync(request.SessionId, presentedTokenHash: null, ct);
        // 404 cả khi phiên thuộc người khác — không để lộ ID phiên có tồn tại.
        if (family is null || family.UserId != userId)
            throw new NotFoundException($"Session '{request.SessionId}' was not found.");
        if (family.Status != SessionStatus.Active) return;

        family.Revoke(SessionRevokeReason.UserRevoked, _time.GetUtcNow());
        _audit.Record(AuditActions.SessionRevoke, AuditResult.Succeeded,
            resourceType: nameof(SessionFamily), resourceId: family.Id.ToString());
        _cacheInvalidator.InvalidateSession(family.Id);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

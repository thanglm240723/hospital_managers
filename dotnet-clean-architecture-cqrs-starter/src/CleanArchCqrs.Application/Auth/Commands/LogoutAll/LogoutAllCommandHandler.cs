using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.LogoutAll;

public sealed class LogoutAllCommandHandler : IRequestHandler<LogoutAllCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly ISessionRepository _sessions;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public LogoutAllCommandHandler(ICurrentUser currentUser, ISessionRepository sessions, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _sessions = sessions;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(LogoutAllCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var now = _time.GetUtcNow();

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var families = await _sessions.GetActiveByUserForUpdateAsync(userId, ct);
        foreach (var family in families)
        {
            family.Revoke(SessionRevokeReason.LogoutAll, now);
            _cacheInvalidator.InvalidateSession(family.Id);
        }
        _audit.Record(AuditActions.LogoutAll, AuditResult.Succeeded,
            metadata: new Dictionary<string, object?> { ["sessions"] = families.Count });
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

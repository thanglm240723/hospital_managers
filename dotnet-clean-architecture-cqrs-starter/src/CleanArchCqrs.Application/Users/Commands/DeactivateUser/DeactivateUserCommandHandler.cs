using CleanArchCqrs.Application.Auth;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.DeactivateUser;

/// Khoá tài khoản: SecurityVersion++ và thu hồi mọi phiên trong cùng khoá hàng (Đặc tả kỹ thuật §4.3).
public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ISessionRepository _sessions;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public DeactivateUserCommandHandler(ICurrentUser currentUser, IUserRepository users, IRoleRepository roles,
        ISessionRepository sessions, ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork,
        TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _roles = roles;
        _sessions = sessions;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(DeactivateUserCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        if (request.UserId == actorId)
            throw new ConflictException(ErrorCodes.SelfActionForbidden, "Không thể tự khoá tài khoản của chính mình.");

        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");
        if (!user.IsActive) return;
        var adminRole = await AdminSafety.GetAdminRoleAsync(_roles, ct);
        await AdminSafety.EnsureNotLastActiveAdminAsync(_users, adminRole.Id, user, ct);

        var now = _time.GetUtcNow();
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        foreach (var family in await _sessions.GetActiveByUserForUpdateAsync(user.Id, ct))
        {
            family.Revoke(SessionRevokeReason.AccountDeactivated, now);
            _cacheInvalidator.InvalidateSession(family.Id);
        }
        user.Deactivate();
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserDeactivate, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

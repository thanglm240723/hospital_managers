using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.GrantUserPermission;

public sealed class GrantUserPermissionCommandHandler : IRequestHandler<GrantUserPermissionCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public GrantUserPermissionCommandHandler(ICurrentUser currentUser, IUserRepository users, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(GrantUserPermissionCommand request, CancellationToken ct)
    {
        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");

        user.GrantPermission(request.PermissionCode, request.Reason, _currentUser.UserId, _time.GetUtcNow());
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserPermissionGrant, AuditResult.Succeeded, reason: request.Reason,
            resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["permission"] = request.PermissionCode });
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

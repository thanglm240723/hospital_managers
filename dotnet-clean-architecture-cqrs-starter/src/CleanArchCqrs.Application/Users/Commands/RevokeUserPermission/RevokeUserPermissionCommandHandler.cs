using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.RevokeUserPermission;

public sealed class RevokeUserPermissionCommandHandler : IRequestHandler<RevokeUserPermissionCommand>
{
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RevokeUserPermissionCommandHandler(IUserRepository users, ICacheInvalidator cacheInvalidator, IAuditRecorder audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(RevokeUserPermissionCommand request, CancellationToken ct)
    {
        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");

        user.RevokePermission(request.PermissionCode);
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserPermissionRevoke, AuditResult.Succeeded, reason: request.Reason,
            resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["permission"] = request.PermissionCode });
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

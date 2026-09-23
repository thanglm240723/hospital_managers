using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;

public sealed class SetRolePermissionsCommandHandler : IRequestHandler<SetRolePermissionsCommand>
{
    private readonly IRoleRepository _roles;
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public SetRolePermissionsCommandHandler(IRoleRepository roles, IUserRepository users, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(SetRolePermissionsCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct)
                   ?? throw new NotFoundException($"Role '{request.RoleId}' was not found.");

        role.SetPermissions(request.PermissionCodes);
        foreach (var userId in await _users.GetUserIdsInRoleAsync(role.Id, ct))
            _cacheInvalidator.InvalidatePermissions(userId);
        _audit.Record(AuditActions.RolePermissionsSet, AuditResult.Succeeded, resourceType: nameof(Role), resourceId: role.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["permissions"] = request.PermissionCodes });
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

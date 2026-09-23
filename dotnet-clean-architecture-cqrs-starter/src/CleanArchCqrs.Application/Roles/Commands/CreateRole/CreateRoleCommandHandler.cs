using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.CreateRole;

public sealed class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IRoleRepository _roles;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CreateRoleCommandHandler(IRoleRepository roles, IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        if (await _roles.CodeExistsAsync(request.Code, ct))
            throw new ConflictException(ErrorCodes.Conflict, "Mã vai trò đã tồn tại.");

        var role = Role.Create(request.Code, request.Name);
        role.SetPermissions(request.PermissionCodes);
        await _roles.AddAsync(role, ct);
        _audit.Record(AuditActions.RoleCreate, AuditResult.Succeeded, resourceType: nameof(Role), resourceId: role.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        return role.Id;
    }
}

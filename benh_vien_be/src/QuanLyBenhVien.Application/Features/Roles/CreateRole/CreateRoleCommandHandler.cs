using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Roles.CreateRole;

internal sealed class CreateRoleCommandHandler(
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<CreateRoleCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(CreateRoleCommand request, CancellationToken cancellationToken)
    {
        if (await roles.CodeExistsAsync(request.Code, cancellationToken))
        {
            return RolesErrors.CodeTaken;
        }

        var role = Role.Create(request.Code, request.Name);
        role.SetPermissions(request.PermissionCodes);
        await roles.AddAsync(role, cancellationToken);
        auditWriter.Record(AuditActions.RoleCreate, AuditResult.Succeeded,
            resourceType: "Role", resourceId: role.Id.ToString(), actorId: currentUser.UserId);

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (UniqueConstraintViolationException ex) when (ex.ConstraintName == RolesErrors.CodeUniqueConstraint)
        {
            return RolesErrors.CodeTaken;
        }

        return RolesErrors.ToDto(role);
    }
}

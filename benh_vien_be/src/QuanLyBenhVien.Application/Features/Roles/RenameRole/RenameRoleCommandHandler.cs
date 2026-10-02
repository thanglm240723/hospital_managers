using MediatR;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Roles.RenameRole;

internal sealed class RenameRoleCommandHandler(
    IRoleRepository roles,
    IUnitOfWork unitOfWork,
    IAuditWriter auditWriter,
    ICurrentUser currentUser)
    : IRequestHandler<RenameRoleCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(RenameRoleCommand request, CancellationToken cancellationToken)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        var role = await roles.GetForUpdateAsync(request.Id, cancellationToken);
        if (role is null)
        {
            return RolesErrors.NotFound;
        }

        if (role.RowVersion != request.ExpectedVersion)
        {
            return RolesErrors.VersionConflict;
        }

        role.Rename(request.Name);
        auditWriter.Record(AuditActions.RoleRename, AuditResult.Succeeded,
            resourceType: "Role", resourceId: role.Id.ToString(), actorId: currentUser.UserId);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return RolesErrors.ToDto(role);
    }
}

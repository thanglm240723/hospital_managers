using MediatR;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Roles.SetRolePermissions;

internal sealed class SetRolePermissionsCommandHandler(
    IRoleRepository roles,
    IUserRepository users,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    ICurrentUser currentUser,
    ILogger<SetRolePermissionsCommandHandler> logger)
    : IRequestHandler<SetRolePermissionsCommand, Result<RoleDto>>
{
    public async Task<Result<RoleDto>> Handle(SetRolePermissionsCommand request, CancellationToken cancellationToken)
    {
        RoleDto dto;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // Thứ tự khoá toàn cục: admin-safety → Roles → Users. Khoá role TRƯỚC khi đọc danh sách member
            // để gán role chen giữa không làm member mới giữ cache sai.
            await users.AcquireAdminSafetyLockAsync(cancellationToken);
            var role = await roles.GetForUpdateAsync(request.Id, cancellationToken);
            if (role is null)
            {
                return RolesErrors.NotFound;
            }

            if (role.RowVersion != request.ExpectedVersion)
            {
                return RolesErrors.VersionConflict;
            }

            var target = request.PermissionCodes.ToHashSet(StringComparer.Ordinal);
            if (role.Code == SystemRoles.Admin && !QuanLyBenhVien.Domain.Identity.Permissions.IdentityAccess.All(p => target.Contains(p.Code)))
            {
                return RolesErrors.AdminCorePermissionsRequired;
            }

            if (role.SetPermissions(target))
            {
                // Bảng con RolePermissions không tự tăng xmin của Role: buộc UPDATE bản ghi cha.
                roles.MarkChanged(role);
                foreach (var userId in await users.GetUserIdsInRoleAsync(role.Id, cancellationToken))
                {
                    cacheInvalidator.InvalidatePermissions(userId);
                }
            }

            auditWriter.Record(AuditActions.RoleSetPermissions, AuditResult.Succeeded,
                resourceType: "Role", resourceId: role.Id.ToString(), actorId: currentUser.UserId);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            dto = RolesErrors.ToDto(role);
            await transaction.CommitAsync(cancellationToken);
        }

        try
        {
            await cacheInvalidator.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flush cache invalidation sau đổi quyền vai trò thất bại; worker sẽ xử lý lại.");
        }

        return dto;
    }
}

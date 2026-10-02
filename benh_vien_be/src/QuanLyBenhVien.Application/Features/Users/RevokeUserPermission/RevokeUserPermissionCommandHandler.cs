using MediatR;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Users.RevokeUserPermission;

/// Thu hồi quyền lẻ (chỉ grant trực tiếp; quyền đến từ role giữ nguyên). Không có grant ⇒ idempotent, không đổi.
internal sealed class RevokeUserPermissionCommandHandler(
    IUserRepository users,
    IUsersReadService readService,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<RevokeUserPermissionCommandHandler> logger)
    : IRequestHandler<RevokeUserPermissionCommand, Result<UserDetailDto>>
{
    public async Task<Result<UserDetailDto>> Handle(RevokeUserPermissionCommand request, CancellationToken cancellationToken)
    {
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            var user = await users.GetWithAccessForUpdateAsync(request.Id, cancellationToken);
            if (user is null)
            {
                return UsersErrors.NotFound;
            }

            if (user.RowVersion != request.ExpectedVersion)
            {
                return UsersErrors.VersionConflict;
            }

            var before = user.PermissionGrants.Count;
            user.RevokePermission(request.PermissionCode, time.GetUtcNow());
            if (user.PermissionGrants.Count != before)
            {
                cacheInvalidator.InvalidatePermissions(user.Id);
            }

            auditWriter.Record(AuditActions.UserPermissionRevoke, AuditResult.Succeeded, reason: request.Reason.Trim(),
                resourceType: "User", resourceId: user.Id.ToString(), actorId: currentUser.UserId,
                metadata: new Dictionary<string, object?> { ["permissionCode"] = request.PermissionCode });

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await UserCommandCompletion.FlushAndReadAsync(cacheInvalidator, readService, logger, request.Id, cancellationToken);
    }
}

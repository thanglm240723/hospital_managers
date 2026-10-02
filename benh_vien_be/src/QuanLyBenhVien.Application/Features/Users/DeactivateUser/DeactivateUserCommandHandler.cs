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
using QuanLyBenhVien.Domain.Identity.Sessions;

namespace QuanLyBenhVien.Application.Features.Users.DeactivateUser;

/// Đặt trạng thái đích "đã khoá": tăng SecurityVersion, thu hồi mọi family đang hoạt động, xoá cache session + quyền
/// (ghi trong cùng transaction, flush sau commit).
internal sealed class DeactivateUserCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    ISessionRepository sessions,
    IUsersReadService readService,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<DeactivateUserCommandHandler> logger)
    : IRequestHandler<DeactivateUserCommand, Result<UserDetailDto>>
{
    public async Task<Result<UserDetailDto>> Handle(DeactivateUserCommand request, CancellationToken cancellationToken)
    {
        var adminRole = await roles.GetByCodeAsync(SystemRoles.Admin, cancellationToken);

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // Thứ tự khoá toàn cục: admin-safety → Users → SessionFamilies.
            await users.AcquireAdminSafetyLockAsync(cancellationToken);
            var user = await users.GetWithAccessForUpdateAsync(request.Id, cancellationToken);
            if (user is null)
            {
                return UsersErrors.NotFound;
            }

            if (user.Id == currentUser.UserId)
            {
                return UsersErrors.SelfActionForbidden;
            }

            if (user.IsActive)
            {
                if (adminRole is not null && user.HasRole(adminRole.Id)
                    && await AdminSafetyGuard.CheckLosesAdminAsync(users, user, adminRole.Id, currentUser.UserId, cancellationToken) is { } error)
                {
                    return error;
                }

                var now = time.GetUtcNow();
                user.Deactivate(now);
                foreach (var family in await sessions.GetActiveByUserForUpdateAsync(user.Id, cancellationToken))
                {
                    family.Revoke(SessionRevokeReason.AccountDeactivated, now);
                    cacheInvalidator.InvalidateSession(family.Id);
                }

                cacheInvalidator.InvalidatePermissions(user.Id);
            }

            auditWriter.Record(AuditActions.UserDeactivate, AuditResult.Succeeded,
                resourceType: "User", resourceId: user.Id.ToString(), actorId: currentUser.UserId);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        return await UserCommandCompletion.FlushAndReadAsync(cacheInvalidator, readService, logger, request.Id, cancellationToken);
    }
}

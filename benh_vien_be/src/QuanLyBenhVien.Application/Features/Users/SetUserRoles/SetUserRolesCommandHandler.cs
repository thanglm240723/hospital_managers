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

namespace QuanLyBenhVien.Application.Features.Users.SetUserRoles;

/// Thay nguyên tập role. Thứ tự khoá toàn cục: admin-safety → Roles (cũ ∪ mới, Id tăng) → User.
/// Admin-safety tuần tự hoá với SetRolePermissions (cũng lấy khoá này trước) để member mới không giữ cache quyền cũ.
internal sealed class SetUserRolesCommandHandler(
    IUserRepository users,
    IRoleRepository roles,
    IUsersReadService readService,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    ICurrentUser currentUser,
    TimeProvider time,
    ILogger<SetUserRolesCommandHandler> logger)
    : IRequestHandler<SetUserRolesCommand, Result<UserDetailDto>>
{
    private const int MaxAttempts = 3;

    public async Task<Result<UserDetailDto>> Handle(SetUserRolesCommand request, CancellationToken cancellationToken)
    {
        var adminRole = await roles.GetByCodeAsync(SystemRoles.Admin, cancellationToken);

        for (var attempt = 1; ; attempt++)
        {
            var outcome = await TryOnceAsync(request, adminRole?.Id, cancellationToken);
            if (outcome is { } result)
            {
                return result.IsFailure
                    ? result.Error!
                    : await UserCommandCompletion.FlushAndReadAsync(cacheInvalidator, readService, logger, request.Id, cancellationToken);
            }

            if (attempt >= MaxAttempts)
            {
                // Membership liên tục đổi giữa lúc đọc và lúc khoá — không thể xảy ra khi mọi ghi UserRoles đều giữ admin-safety.
                return UsersErrors.VersionConflict;
            }
        }
    }

    /// null ⇒ role cũ đọc trước khi khoá đã đổi, transaction đã rollback; caller thử lại từ đầu.
    private async Task<Result?> TryOnceAsync(SetUserRolesCommand request, Guid? adminRoleId, CancellationToken ct)
    {
        await using var transaction = await unitOfWork.BeginTransactionAsync(ct);
        await users.AcquireAdminSafetyLockAsync(ct);

        var previousRoleIds = await users.GetRoleIdsAsync(request.Id, ct);
        var lockSet = previousRoleIds.Union(request.RoleIds).ToHashSet();
        var lockedRoleIds = (await roles.LockAsync(lockSet, ct)).ToHashSet();
        if (!request.RoleIds.All(lockedRoleIds.Contains))
        {
            return Result.Failure(UsersErrors.UnknownRoles);
        }

        var user = await users.GetWithAccessForUpdateAsync(request.Id, ct);
        if (user is null)
        {
            return Result.Failure(UsersErrors.NotFound);
        }

        // Kiểm lại sau khoá: role đang gán phải nằm trong tập đã khoá; nếu không, rollback và tải lại
        // (không lấy thêm khoá role khi đang giữ User để giữ đúng thứ tự khoá).
        if (!user.RoleAssignments.All(r => lockSet.Contains(r.RoleId)))
        {
            return null;
        }

        if (user.RowVersion != request.ExpectedVersion)
        {
            return Result.Failure(UsersErrors.VersionConflict);
        }

        if (adminRoleId is { } adminId && user.HasRole(adminId) && !request.RoleIds.Contains(adminId)
            && await AdminSafetyGuard.CheckLosesAdminAsync(users, user, adminId, currentUser.UserId, ct) is { } error)
        {
            return Result.Failure(error);
        }

        var before = user.RoleAssignments.Select(r => r.RoleId).ToHashSet();
        user.SetRoles(request.RoleIds, currentUser.UserId, time.GetUtcNow());
        if (!before.SetEquals(request.RoleIds))
        {
            cacheInvalidator.InvalidatePermissions(user.Id);
        }

        auditWriter.Record(AuditActions.UserSetRoles, AuditResult.Succeeded,
            resourceType: "User", resourceId: user.Id.ToString(), actorId: currentUser.UserId);

        await unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Result.Success();
    }
}

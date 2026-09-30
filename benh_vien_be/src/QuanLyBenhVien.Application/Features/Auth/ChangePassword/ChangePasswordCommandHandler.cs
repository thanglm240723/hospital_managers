using MediatR;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;

namespace QuanLyBenhVien.Application.Features.Auth.ChangePassword;

internal sealed class ChangePasswordCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    TimeProvider time,
    ILogger<ChangePasswordCommandHandler> logger)
    : IRequestHandler<ChangePasswordCommand, Result<AccessTokenDto>>
{
    public async Task<Result<AccessTokenDto>> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId
            || currentUser.SessionFamilyId is not { } familyId
            || currentUser.SecurityVersion is not { } securityVersion)
        {
            return AuthErrors.Unauthenticated;
        }

        User user;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // Thứ tự khoá thống nhất (spec V2 §5.1): User → SessionFamilies theo Id tăng. Mọi kiểm tra dựa trên dữ liệu SAU khoá.
            var locked = await users.GetForUpdateAsync(userId, cancellationToken);
            if (locked is null || !locked.IsActive || locked.SecurityVersion != securityVersion)
            {
                return AuthErrors.Unauthenticated;
            }

            user = locked;
            var now = time.GetUtcNow();
            var families = await sessions.GetActiveByUserForUpdateAsync(userId, cancellationToken);
            var current = families.FirstOrDefault(f => f.Id == familyId);
            if (current is null || !current.IsActiveAt(now))
            {
                return AuthErrors.Unauthenticated;
            }

            if (!passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            {
                // Audit thất bại phải bền vững trước khi trả lỗi.
                auditWriter.Record(AuditActions.PasswordChange, AuditResult.Failed, "InvalidCurrentPassword", actorId: user.Id);
                await unitOfWork.SaveChangesAsync(CancellationToken.None);
                await transaction.CommitAsync(CancellationToken.None);
                return ChangePasswordErrors.InvalidCurrentPassword;
            }

            if (passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            {
                return ChangePasswordErrors.SameAsCurrent;
            }

            if (PasswordPolicy.ContainsEmailLocalPart(request.NewPassword, user.Email))
            {
                return ChangePasswordErrors.ContainsEmailName;
            }

            user.ChangePassword(passwordHasher.Hash(request.NewPassword), now);
            foreach (var family in families)
            {
                if (family.Id != current.Id)
                {
                    family.Revoke(SessionRevokeReason.PasswordChanged, now);
                }

                // Kể cả phiên hiện tại: entry cache cũ mang sv cũ. Không ghi lại session:{currentFid} ở đây.
                cacheInvalidator.InvalidateSession(family.Id);
            }

            cacheInvalidator.InvalidatePermissions(user.Id);
            auditWriter.Record(AuditActions.PasswordChange, AuditResult.Succeeded,
                resourceType: "User", resourceId: user.Id.ToString(), actorId: user.Id);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        // Sau commit: mật khẩu đã đổi. Flush lỗi/huỷ không được biến kết quả thành lỗi — worker xử lý dòng còn lại.
        try
        {
            await cacheInvalidator.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flush cache invalidation sau đổi mật khẩu thất bại; worker sẽ xử lý lại.");
        }

        var access = accessTokens.Issue(user.Id, familyId, user.SecurityVersion);
        return new AccessTokenDto(access.Value, access.ExpiresAtUtc, false);
    }
}

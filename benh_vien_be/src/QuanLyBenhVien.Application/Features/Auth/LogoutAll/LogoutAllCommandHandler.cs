using MediatR;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;

namespace QuanLyBenhVien.Application.Features.Auth.LogoutAll;

internal sealed class LogoutAllCommandHandler(
    ICurrentUser currentUser,
    IUserRepository users,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    TimeProvider time,
    ILogger<LogoutAllCommandHandler> logger)
    : IRequestHandler<LogoutAllCommand, Result>
{
    public async Task<Result> Handle(LogoutAllCommand request, CancellationToken cancellationToken)
    {
        // UserId/family/sv lấy từ access token, không từ client.
        if (currentUser.UserId is not { } userId
            || currentUser.SessionFamilyId is not { } familyId
            || currentUser.SecurityVersion is not { } securityVersion)
        {
            return AuthErrors.Unauthenticated;
        }

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // Thứ tự khoá (spec V2 §5.1): User → SessionFamilies theo Id tăng → token.
            var user = await users.GetForUpdateAsync(userId, cancellationToken);
            if (user is null || !user.IsActive || user.SecurityVersion != securityVersion)
            {
                return AuthErrors.Unauthenticated;
            }

            var now = time.GetUtcNow();
            var families = await sessions.GetActiveByUserForUpdateAsync(userId, cancellationToken);
            var current = families.FirstOrDefault(f => f.Id == familyId);
            if (current is null || !current.IsActiveAt(now))
            {
                return AuthErrors.Unauthenticated;
            }

            foreach (var family in families)
            {
                family.Revoke(SessionRevokeReason.LogoutAll, now);
                cacheInvalidator.InvalidateSession(family.Id);
            }

            auditWriter.Record(AuditActions.LogoutAll, AuditResult.Succeeded,
                resourceType: "User", resourceId: user.Id.ToString(), actorId: user.Id);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        try
        {
            await cacheInvalidator.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flush cache invalidation sau logout-all thất bại; worker sẽ xử lý lại.");
        }

        return Result.Success();
    }
}

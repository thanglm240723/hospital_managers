using MediatR;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;

namespace QuanLyBenhVien.Application.Features.Auth.Logout;

internal sealed class LogoutCommandHandler(
    IRefreshTokenGenerator tokenGenerator,
    IRefreshSessionLookup sessionLookup,
    IUserRepository users,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    TimeProvider time,
    ILogger<LogoutCommandHandler> logger)
    : IRequestHandler<LogoutCommand, Result>
{
    public async Task<Result> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        // Không nhận diện được family ⇒ không có gì để thu hồi: thành công, không mutate.
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return Result.Success();
        }

        var tokenHash = tokenGenerator.Hash(request.RefreshToken);
        var located = await sessionLookup.FindAsync(tokenHash, cancellationToken);
        if (located is null || located.Status != SessionStatus.Active)
        {
            return Result.Success();
        }

        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // Thứ tự khoá thống nhất (spec V2 §5.1): User → family → token. Kiểm tra lại trên dữ liệu SAU khoá.
            var user = await users.GetForUpdateAsync(located.UserId, cancellationToken);
            var family = await sessions.GetForUpdateAsync(located.FamilyId, tokenHash, cancellationToken);
            if (user is null || family is null || family.UserId != user.Id || family.Status != SessionStatus.Active)
            {
                // Đã bị thu hồi song song (logout lặp/logout-all): idempotent, không audit thành công trùng.
                return Result.Success();
            }

            family.Revoke(SessionRevokeReason.Logout, time.GetUtcNow());
            cacheInvalidator.InvalidateSession(family.Id);
            auditWriter.Record(AuditActions.Logout, AuditResult.Succeeded,
                resourceType: "SessionFamily", resourceId: family.Id.ToString(), actorId: user.Id);

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        try
        {
            await cacheInvalidator.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flush cache invalidation sau logout thất bại; worker sẽ xử lý lại.");
        }

        return Result.Success();
    }
}

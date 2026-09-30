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

namespace QuanLyBenhVien.Application.Features.Auth.RefreshSession;

internal sealed class RefreshSessionCommandHandler(
    IRefreshTokenGenerator tokenGenerator,
    IRefreshSessionLookup sessionLookup,
    IUserRepository users,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IAccessTokenIssuer accessTokens,
    ICsrfTokenService csrf,
    ICacheInvalidator cacheInvalidator,
    IAuditWriter auditWriter,
    TimeProvider time,
    ILogger<RefreshSessionCommandHandler> logger)
    : IRequestHandler<RefreshSessionCommand, Result<AuthTokensResult>>
{
    public async Task<Result<AuthTokensResult>> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        // Thiếu/rác cookie ⇒ unauthorized (không phải lỗi validation 400).
        if (string.IsNullOrEmpty(request.RefreshToken))
        {
            return AuthErrors.Unauthenticated;
        }

        var presentedHash = tokenGenerator.Hash(request.RefreshToken);
        var located = await sessionLookup.FindAsync(presentedHash, cancellationToken);
        if (located is null || located.Status != SessionStatus.Active)
        {
            // Family đã revoked: không đổi reason, không ghi reuse.
            return AuthErrors.Unauthenticated;
        }

        User user;
        SessionFamily family;
        GeneratedRefreshToken next;
        await using (var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken))
        {
            // Thứ tự khoá thống nhất (spec V2 §5.1): User → family → token. Kiểm tra lại trên dữ liệu SAU khoá.
            var lockedUser = await users.GetForUpdateAsync(located.UserId, cancellationToken);
            var lockedFamily = await sessions.GetForUpdateAsync(located.FamilyId, presentedHash, cancellationToken);
            if (lockedUser is null || !lockedUser.IsActive || lockedFamily is null || lockedFamily.UserId != lockedUser.Id)
            {
                return AuthErrors.Unauthenticated;
            }

            user = lockedUser;
            family = lockedFamily;
            next = tokenGenerator.Generate();
            var now = time.GetUtcNow();

            switch (family.Rotate(presentedHash, next.Hash, now))
            {
                case RotationResult.Rotated:
                    break;

                case RotationResult.ReuseDetected:
                    // Strict reuse: thu hồi family, audit và invalidation phải COMMIT trước khi trả 401.
                    cacheInvalidator.InvalidateSession(family.Id);
                    auditWriter.Record(AuditActions.RefreshReuse, AuditResult.Denied, "RefreshTokenReuse",
                        resourceType: "SessionFamily", resourceId: family.Id.ToString(), actorId: user.Id);
                    await unitOfWork.SaveChangesAsync(CancellationToken.None);
                    await transaction.CommitAsync(CancellationToken.None);
                    await FlushAsync();
                    return AuthErrors.Unauthenticated;

                default:
                    // NotActive/Expired: không rotate, không mutate (rollback khi dispose).
                    return AuthErrors.Unauthenticated;
            }

            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            await transaction.CommitAsync(CancellationToken.None);
        }

        // sv lấy từ User đã khoá ⇒ luôn là giá trị mới nhất (đổi mật khẩu song song đã commit trước).
        var access = accessTokens.Issue(user.Id, family.Id, user.SecurityVersion);
        return new AuthTokensResult(
            access.Value,
            access.ExpiresAtUtc,
            next.Token,
            family.AbsoluteExpiresAtUtc,
            csrf.Create(family.Id),
            user.MustChangePassword);
    }

    private async Task FlushAsync()
    {
        try
        {
            await cacheInvalidator.FlushAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Flush cache invalidation sau phát hiện reuse refresh token thất bại; worker sẽ xử lý lại.");
        }
    }
}

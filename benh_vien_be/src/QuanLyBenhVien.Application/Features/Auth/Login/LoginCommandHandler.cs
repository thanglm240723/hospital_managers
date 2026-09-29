using MediatR;
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

namespace QuanLyBenhVien.Application.Features.Auth.Login;

internal sealed class LoginCommandHandler(
    IUserRepository users,
    ISessionRepository sessions,
    IUnitOfWork unitOfWork,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    IRefreshTokenGenerator refreshTokens,
    ICsrfTokenService csrf,
    ISessionCache sessionCache,
    IAuditWriter auditWriter,
    IRequestContext requestContext,
    TimeProvider time)
    : IRequestHandler<LoginCommand, Result<AuthTokensResult>>
{
    public async Task<Result<AuthTokensResult>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var email = User.NormalizeEmail(request.Email);

        // Tra không khoá để định vị user (có thể null). Việc kiểm tra hash/IsActive thật sự diễn ra
        // SAU khi khoá hàng bên dưới, để tránh đăng nhập được bằng dữ liệu vừa đổi (vd. mật khẩu vừa bị đổi).
        var located = await users.GetByEmailAsync(email, cancellationToken);
        if (located is null)
        {
            passwordHasher.SimulateVerify(request.Password);
            auditWriter.Record(AuditActions.Login, AuditResult.Failed, "EmailNotFound");
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return AuthErrors.InvalidCredentials;
        }

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);
        var user = await users.GetForUpdateAsync(located.Id, cancellationToken);

        var reason = CheckCredentials(user, request.Password);
        if (reason is not null)
        {
            auditWriter.Record(AuditActions.Login, AuditResult.Failed, reason, actorId: user?.Id ?? located.Id);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AuthErrors.InvalidCredentials;
        }

        var now = time.GetUtcNow();
        var refresh = refreshTokens.Generate();
        var family = SessionFamily.Start(user!.Id, refresh.Hash, now, requestContext.IpAddress, requestContext.UserAgent);

        user.RecordLogin(now);
        await sessions.AddAsync(family, cancellationToken);
        auditWriter.Record(AuditActions.Login, AuditResult.Succeeded, resourceType: "SessionFamily", resourceId: family.Id.ToString(), actorId: user.Id);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Sau commit: không gọi Redis/HTTP ngoài trong lúc giữ transaction.
        var entry = new SessionCacheEntry(family.Id, user.Id, user.SecurityVersion, family.AbsoluteExpiresAtUtc);
        await sessionCache.SetIfGenerationUnchangedAsync(entry, CacheGeneration.None, cancellationToken);

        var access = accessTokens.Issue(user.Id, family.Id, user.SecurityVersion);
        var csrfToken = csrf.Create(family.Id);

        return new AuthTokensResult(
            access.Value,
            access.ExpiresAtUtc,
            refresh.Token,
            family.AbsoluteExpiresAtUtc,
            csrfToken,
            user.MustChangePassword);
    }

    private string? CheckCredentials(User? user, string password)
    {
        if (user is null)
        {
            passwordHasher.SimulateVerify(password);
            return "EmailNotFound";
        }

        if (!passwordHasher.Verify(password, user.PasswordHash))
        {
            return "InvalidPassword";
        }

        if (!user.IsActive)
        {
            return "AccountInactive";
        }

        return null;
    }
}

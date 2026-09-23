using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthSession>
{
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly ISessionCache _sessionCache;
    private readonly IAuditRecorder _audit;
    private readonly IRequestContext _request;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public LoginCommandHandler(IUserRepository users, ISessionRepository sessions, IPasswordHasher passwordHasher,
        ITokenService tokenService, IRefreshTokenGenerator refreshTokens, ILoginRateLimiter rateLimiter,
        ISessionCache sessionCache, IAuditRecorder audit, IRequestContext request, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _users = users;
        _sessions = sessions;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _rateLimiter = rateLimiter;
        _sessionCache = sessionCache;
        _audit = audit;
        _request = request;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<AuthSession> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);

        var lockout = await _rateLimiter.GetLockoutRemainingAsync(email, ct);
        if (lockout is not null)
        {
            _audit.Record(AuditActions.RateLimited, AuditResult.Denied, reason: "EmailLockout");
            await _unitOfWork.SaveChangesAsync(ct);
            throw new TooManyRequestsException(lockout.Value, AuthMessages.TooManyAttempts);
        }

        var user = await _users.GetByEmailAsync(email, ct);
        var failure = FindFailure(user, request.Password);
        if (user is null || failure is not null)
        {
            await _rateLimiter.RegisterFailureAsync(email, ct);
            _audit.Record(AuditActions.Login, AuditResult.Failed, reason: failure,
                resourceType: nameof(User), resourceId: user?.Id.ToString(), actorId: user?.Id);
            await _unitOfWork.SaveChangesAsync(ct);   // lưu vết TRƯỚC khi ném
            throw new UnauthorizedException(AuthMessages.LoginFailed);
        }

        await _rateLimiter.ResetAsync(email, ct);
        var refresh = _refreshTokens.Generate();
        var family = SessionFamily.Start(user.Id, refresh.Hash, _time.GetUtcNow(), _request.IpAddress, _request.UserAgent);
        user.RecordLogin();
        await _sessions.AddAsync(family, ct);
        _audit.Record(AuditActions.Login, AuditResult.Succeeded,
            resourceType: nameof(SessionFamily), resourceId: family.Id.ToString(), actorId: user.Id);
        await _unitOfWork.SaveChangesAsync(ct);   // một lần commit cho RecordLogin + family + audit

        await _sessionCache.SetAsync(
            new SessionCacheEntry(family.Id, user.Id, user.SecurityVersion, family.AbsoluteExpiresAtUtc), ct);

        var access = _tokenService.CreateAccessToken(user.Id, family.Id, user.SecurityVersion);
        return new AuthSession(access.Token, access.ExpiresAtUtc, user.MustChangePassword,
            refresh.Token, family.Id, family.AbsoluteExpiresAtUtc);
    }

    private string? FindFailure(User? user, string password)
    {
        if (user is null)
        {
            _passwordHasher.SimulateVerify(password);   // cân thời gian với nhánh sai mật khẩu
            return "EmailNotFound";
        }
        if (!_passwordHasher.Verify(password, user.PasswordHash)) return "InvalidPassword";
        if (!user.IsActive) return "AccountInactive";
        return null;
    }
}

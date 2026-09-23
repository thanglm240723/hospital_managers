using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Refresh;

/// Đặc tả kỹ thuật §4.2: trong transaction, khoá family → token, xoay vòng hoặc phát hiện reuse.
public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, AuthSession>
{
    private readonly ISessionRepository _sessions;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ITokenService _tokenService;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public RefreshCommandHandler(ISessionRepository sessions, IUserRepository users, IRefreshTokenGenerator refreshTokens,
        ITokenService tokenService, ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork,
        TimeProvider time)
    {
        _sessions = sessions;
        _users = users;
        _refreshTokens = refreshTokens;
        _tokenService = tokenService;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<AuthSession> Handle(RefreshCommand request, CancellationToken ct)
    {
        var presentedHash = _refreshTokens.Hash(request.RefreshToken);
        var now = _time.GetUtcNow();

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var familyId = await _sessions.FindFamilyIdByTokenHashAsync(presentedHash, ct)
            ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var family = await _sessions.GetForUpdateAsync(familyId, presentedHash, ct)
            ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var user = await _users.GetUserByIdAsync(family.UserId, ct);
        if (!user.IsActive) throw new UnauthorizedException(AuthMessages.SessionInvalid);

        var next = _refreshTokens.Generate();
        var result = family.Rotate(presentedHash, next.Hash, now);

        if (result == RotationResult.ReuseDetected)
        {
            _audit.Record(AuditActions.RefreshReuse, AuditResult.Denied,
                resourceType: nameof(SessionFamily), resourceId: family.Id.ToString(), actorId: family.UserId);
            _cacheInvalidator.InvalidateSession(family.Id);
            await _unitOfWork.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);   // commit việc thu hồi TRƯỚC khi trả 401 — không để exception rollback nó
            await _cacheInvalidator.FlushAsync(ct);
            throw new UnauthorizedException(AuthMessages.SessionInvalid);
        }

        if (result != RotationResult.Rotated)
            throw new UnauthorizedException(AuthMessages.SessionInvalid);

        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);   // commit rồi mới phát token

        var access = _tokenService.CreateAccessToken(user.Id, family.Id, user.SecurityVersion);
        return new AuthSession(access.Token, access.ExpiresAtUtc, user.MustChangePassword,
            next.Token, family.Id, family.AbsoluteExpiresAtUtc);
    }
}

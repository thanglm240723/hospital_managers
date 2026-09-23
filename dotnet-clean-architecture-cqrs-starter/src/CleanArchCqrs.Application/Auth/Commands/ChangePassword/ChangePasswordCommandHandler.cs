using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Security;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, AccessTokenResult>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public ChangePasswordCommandHandler(ICurrentUser currentUser, IUserRepository users, ISessionRepository sessions,
        IPasswordHasher passwordHasher, ITokenService tokenService, ICacheInvalidator cacheInvalidator,
        ILoginRateLimiter rateLimiter, IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _sessions = sessions;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _cacheInvalidator = cacheInvalidator;
        _rateLimiter = rateLimiter;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<AccessTokenResult> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var currentFamilyId = _currentUser.SessionFamilyId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var user = await _users.GetUserByIdAsync(userId, ct);

        // Cùng hàng rào rate-limit với login (khoá theo email chuẩn hoá) — nếu không, kẻ giữ access token
        // (XSS, hoặc token bị đánh cắp trong 15 phút) có thể dò mật khẩu hiện tại không giới hạn số lần.
        var lockout = await _rateLimiter.GetLockoutRemainingAsync(user.Email, ct);
        if (lockout is not null)
        {
            _audit.Record(AuditActions.RateLimited, AuditResult.Denied, reason: "EmailLockout",
                resourceType: nameof(User), resourceId: user.Id.ToString(), actorId: user.Id);
            await _unitOfWork.SaveChangesAsync(ct);
            throw new TooManyRequestsException(lockout.Value, AuthMessages.TooManyAttempts);
        }

        // Người dùng đang đăng nhập hợp lệ — sai mật khẩu hiện tại là lỗi dữ liệu (400), không phải 401.
        // Vẫn phải đăng ký thất bại + ghi audit TRƯỚC khi ném, theo đúng mẫu của LoginCommandHandler.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
        {
            await _rateLimiter.RegisterFailureAsync(user.Email, ct);
            _audit.Record(AuditActions.PasswordChange, AuditResult.Failed, reason: "InvalidCurrentPassword",
                resourceType: nameof(User), resourceId: user.Id.ToString(), actorId: user.Id);
            await _unitOfWork.SaveChangesAsync(ct);   // lưu vết TRƯỚC khi ném
            throw new ValidationException(nameof(request.CurrentPassword), "Mật khẩu hiện tại không đúng.");
        }
        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            throw new ValidationException(nameof(request.NewPassword), "Mật khẩu mới phải khác mật khẩu hiện tại.");
        if (PasswordPolicy.ContainsEmailLocalPart(request.NewPassword, user.Email))
            throw new ValidationException(nameof(request.NewPassword), "Mật khẩu không được chứa phần tên trong email.");

        await _rateLimiter.ResetAsync(user.Email, ct);

        var now = _time.GetUtcNow();
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var families = await _sessions.GetActiveByUserForUpdateAsync(user.Id, ct);
        if (families.All(f => f.Id != currentFamilyId))
            throw new UnauthorizedException(AuthMessages.SessionInvalid);

        foreach (var family in families.Where(f => f.Id != currentFamilyId))
            family.Revoke(SessionRevokeReason.PasswordChanged, now);
        foreach (var family in families)
            _cacheInvalidator.InvalidateSession(family.Id);   // kể cả phiên hiện tại: key cũ mang sv cũ
        user.ChangePassword(_passwordHasher.Hash(request.NewPassword));   // SecurityVersion++, MustChangePassword=false
        _cacheInvalidator.InvalidatePermissions(user.Id);                 // perm:{uid} lưu cờ mustChangePassword
        _audit.Record(AuditActions.PasswordChange, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);

        var access = _tokenService.CreateAccessToken(user.Id, currentFamilyId, user.SecurityVersion);
        return new AccessTokenResult(access.Token, access.ExpiresAtUtc, user.MustChangePassword);
    }
}

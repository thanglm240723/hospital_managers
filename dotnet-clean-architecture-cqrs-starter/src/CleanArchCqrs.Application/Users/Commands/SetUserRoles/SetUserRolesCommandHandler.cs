using CleanArchCqrs.Application.Auth;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.SetUserRoles;

public sealed class SetUserRolesCommandHandler : IRequestHandler<SetUserRolesCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public SetUserRolesCommandHandler(ICurrentUser currentUser, IUserRepository users, IRoleRepository roles,
        ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _roles = roles;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(SetUserRolesCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);

        var roleIds = request.RoleIds.Distinct().ToList();
        if ((await _roles.GetByIdsAsync(roleIds, ct)).Count != roleIds.Count)
            throw new ValidationException(nameof(request.RoleIds), "Có vai trò không tồn tại.");

        var adminRole = await AdminSafety.GetAdminRoleAsync(_roles, ct);
        var now = _time.GetUtcNow();

        // Luôn mở transaction và lấy khoá tư vấn VÔ ĐIỀU KIỆN trước khi nạp user, rồi mới quyết định có
        // mất role admin hay không từ entity nạp SAU khoá. Nếu quyết định dựa vào entity nạp TRƯỚC khoá
        // (hoặc chỉ lấy khoá khi "nhìn thấy" mất admin), một request khác có thể vừa commit đổi trạng thái
        // IsActive/role admin của user này ngay trước đó — guard sẽ dùng dữ liệu cũ và bỏ qua kiểm tra dù
        // bất biến "còn ≥ 1 admin đang hoạt động" đang bị đe doạ (xem AdminSafety).
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        await _users.AcquireAdminSafetyLockAsync(ct);
        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");

        var losesAdmin = user.HasRole(adminRole.Id) && !roleIds.Contains(adminRole.Id);
        if (losesAdmin)
        {
            if (user.Id == actorId)
                throw new ConflictException(ErrorCodes.SelfActionForbidden, "Không thể tự gỡ vai trò quản trị của chính mình.");
            await AdminSafety.EnsureNotLastActiveAdminAsync(_users, adminRole.Id, user, ct);
        }

        user.SetRoles(roleIds, actorId, now);
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserRolesSet, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["roleIds"] = roleIds });
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

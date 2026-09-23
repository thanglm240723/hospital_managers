using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;

public sealed class SetRolePermissionsCommandHandler : IRequestHandler<SetRolePermissionsCommand>
{
    private readonly IRoleRepository _roles;
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public SetRolePermissionsCommandHandler(IRoleRepository roles, IUserRepository users, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(SetRolePermissionsCommand request, CancellationToken ct)
    {
        // Khoá tư vấn (dùng chung với SetUserRoles) VÔ ĐIỀU KIỆN trước khi nạp role/holder, rồi mới đọc.
        // Nếu đọc holder TRƯỚC khoá (hoặc không khoá), một SetUserRoles đang chạy song song có thể thêm
        // user vào role này SAU khi ta đã chụp danh sách holder nhưng TRƯỚC khi ta commit — user đó sẽ
        // không được invalidate và giữ quyền cũ (đã bị gỡ) vô thời hạn vì perm:{uid} không có TTL.
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        await _users.AcquireAdminSafetyLockAsync(ct);

        var role = await _roles.GetByIdAsync(request.RoleId, ct)
                   ?? throw new NotFoundException($"Role '{request.RoleId}' was not found.");

        if (role.Code == SystemRoles.Admin)
        {
            var target = request.PermissionCodes.ToHashSet(StringComparer.Ordinal);
            if (!Permissions.IdentityAccess.Select(p => p.Code).All(target.Contains))
                throw new ConflictException(ErrorCodes.Conflict,
                    "Không thể gỡ quyền quản trị hệ thống (IdentityAccess) khỏi vai trò admin.");
        }

        var holders = await _users.GetUserIdsInRoleAsync(role.Id, ct);
        role.SetPermissions(request.PermissionCodes);
        foreach (var userId in holders)
            _cacheInvalidator.InvalidatePermissions(userId);
        _audit.Record(AuditActions.RolePermissionsSet, AuditResult.Succeeded, resourceType: nameof(Role), resourceId: role.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["permissions"] = request.PermissionCodes });
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}

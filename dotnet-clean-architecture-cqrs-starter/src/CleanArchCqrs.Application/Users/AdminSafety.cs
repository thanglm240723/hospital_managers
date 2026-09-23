using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Domain.Identity;

namespace CleanArchCqrs.Application.Users;

/// Quy tắc bảo vệ Spec §4.2: luôn còn ≥ 1 admin đang hoạt động.
internal static class AdminSafety
{
    public static async Task<Role> GetAdminRoleAsync(IRoleRepository roles, CancellationToken ct)
        => await roles.GetByCodeAsync(SystemRoles.Admin, ct)
           ?? throw new InvalidOperationException("The admin role has not been seeded.");

    /// Gọi TRƯỚC khi làm user mất quyền admin (khoá tài khoản hoặc gỡ role admin).
    public static async Task EnsureNotLastActiveAdminAsync(IUserRepository users, Guid adminRoleId, User user, CancellationToken ct)
    {
        if (!user.IsActive || !user.HasRole(adminRoleId)) return;
        if (await users.CountActiveUsersInRoleAsync(adminRoleId, excludingUserId: user.Id, ct) == 0)
            throw new ConflictException(ErrorCodes.LastAdmin, "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.");
    }
}

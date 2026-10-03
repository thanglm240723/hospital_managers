using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Users.Common;

/// Bất biến "luôn còn ≥ 1 admin đang hoạt động" và "không tự khoá/tự gỡ admin". Caller PHẢI đang giữ
/// AcquireAdminSafetyLockAsync và khoá User mục tiêu trong cùng transaction.
internal static class AdminSafetyGuard
{
    public static async Task<Error?> CheckLosesAdminAsync(
        IUserRepository users, User target, Guid adminRoleId, Guid? actorId, CancellationToken ct)
    {
        if (target.Id == actorId)
        {
            return UsersErrors.SelfActionForbidden;
        }

        if (target.IsActive && await users.CountActiveUsersInRoleAsync(adminRoleId, target.Id, ct) == 0)
        {
            return UsersErrors.LastAdmin;
        }

        return null;
    }
}

using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Domain.Identity;

namespace CleanArchCqrs.Application.Users;

/// Quy tắc bảo vệ Spec §4.2: luôn còn ≥ 1 admin đang hoạt động.
internal static class AdminSafety
{
    public static async Task<Role> GetAdminRoleAsync(IRoleRepository roles, CancellationToken ct)
        => await roles.GetByCodeAsync(SystemRoles.Admin, ct)
           ?? throw new InvalidOperationException("The admin role has not been seeded.");

    /// Gọi BÊN TRONG transaction, SAU KHI caller đã gọi <see cref="IUserRepository.AcquireAdminSafetyLockAsync"/>
    /// VÀ nạp lại (reload) user TỪ SAU thời điểm khoá đó. Trước bản sửa này, method tự lấy khoá nhưng vẫn
    /// nhận entity `user` đã nạp TRƯỚC transaction/khoá — nếu entity đó stale ("inactive" hoặc "không còn
    /// role admin" do một request khác vừa commit), điều kiện short-circuit dưới đây bỏ qua guard hoàn
    /// toàn dù bất biến "còn ≥ 1 admin đang hoạt động" đang bị đe doạ. Không tự lấy khoá ở đây nữa — khoá
    /// phải được lấy VÔ ĐIỀU KIỆN ngay khi mở transaction (trước khi biết có cần guard hay không), rồi mới
    /// nạp lại user, để lần đọc `IsActive`/`HasRole` dưới đây luôn thấy dữ liệu mới nhất đã commit.
    public static async Task EnsureNotLastActiveAdminAsync(IUserRepository users, Guid adminRoleId, User user, CancellationToken ct)
    {
        if (!user.IsActive || !user.HasRole(adminRoleId)) return;
        if (await users.CountActiveUsersInRoleAsync(adminRoleId, excludingUserId: user.Id, ct) == 0)
            throw new ConflictException(ErrorCodes.LastAdmin, "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.");
    }
}

using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

/// Một nơi duy nhất tính quyền hiệu lực từ DB (dùng bởi /me và PermissionService).
internal static class EffectivePermissions
{
    public static async Task<UserAccessDto?> LoadAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.MustChangePassword })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;
        if (!user.IsActive) return new UserAccessDto(false, user.MustChangePassword, new HashSet<string>());

        var fromRoles =
            from userRole in db.Set<UserRole>()
            join rolePermission in db.Set<RolePermission>() on userRole.RoleId equals rolePermission.RoleId
            where userRole.UserId == userId
            select rolePermission.PermissionCode;
        var fromGrants = db.Set<UserPermission>().Where(p => p.UserId == userId).Select(p => p.PermissionCode);

        var codes = await fromRoles.Union(fromGrants).ToListAsync(ct);
        return new UserAccessDto(true, user.MustChangePassword, codes.ToHashSet(StringComparer.Ordinal));
    }
}

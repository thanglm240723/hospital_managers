using QuanLyBenhVien.Application.Common.Models;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Infrastructure.Identity;

/// Một nơi duy nhất tính quyền hiệu lực từ DB (dùng bởi /me và PermissionService).
internal static class EffectivePermissionsQuery
{
    public static async Task<UserAccess?> LoadAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.MustChangePassword })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;
        if (!user.IsActive) return new UserAccess(new HashSet<string>(), user.MustChangePassword);

        var fromRoles =
            from userRole in db.Set<UserRole>()
            join rolePermission in db.Set<RolePermission>() on userRole.RoleId equals rolePermission.RoleId
            where userRole.UserId == userId
            select rolePermission.PermissionCode;
        var fromGrants = db.Set<UserPermission>().Where(p => p.UserId == userId).Select(p => p.PermissionCode);

        var codes = await fromRoles.Union(fromGrants).ToListAsync(ct);
        return new UserAccess(codes.ToHashSet(StringComparer.Ordinal), user.MustChangePassword);
    }
}

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Persistence.Seed;

internal sealed class RoleDefaultsApplier(AppDbContext db, ICacheInvalidator cacheInvalidator, TimeProvider time,
    ILogger<RoleDefaultsApplier> logger)
{
    /// Hằng khóa advisory riêng cho seeder quyền mặc định (không trùng khóa admin-safety).
    private const long AdvisoryLockKey = 0x48_4D_53_52_50_44; // "HMSRPD"

    public Task ApplyAsync(CancellationToken ct) => ApplyAsync(DefaultRolePermissions.ByRole, Permissions.IsDefined, ct);

    /// Seam cho test: ma trận và điều kiện "đã kích hoạt" truyền vào được.
    internal async Task ApplyAsync(IReadOnlyDictionary<string, IReadOnlyList<string>> matrix, Func<string, bool> isActivated, CancellationToken ct)
    {
        await using var tx = await db.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdvisoryLockKey})", ct);

        var roles = await db.Roles.Include(r => r.GrantedPermissions).ToDictionaryAsync(r => r.Code, ct);
        var applied = (await db.RolePermissionDefaults
                .Join(db.Roles, d => d.RoleId, r => r.Id, (d, r) => new { r.Code, d.PermissionCode })
                .ToListAsync(ct))
            .Select(x => (x.Code, x.PermissionCode)).ToHashSet();

        var pending = RoleDefaultsPlan.PendingPairs(matrix, isActivated, applied)
            .Where(p => roles.ContainsKey(p.RoleCode)).ToList();
        if (pending.Count == 0) return;

        var now = time.GetUtcNow();
        foreach (var group in pending.GroupBy(p => p.RoleCode))
        {
            var role = roles[group.Key];
            var current = role.GrantedPermissions.Select(p => p.PermissionCode).ToList();
            if (role.SetPermissions(current.Union(group.Select(p => p.PermissionCode))))
            {
                db.Entry(role).Property(r => r.Name).IsModified = true;
                var holders = await db.Set<UserRole>().Where(r => r.RoleId == role.Id).Select(r => r.UserId).ToListAsync(ct);
                foreach (var userId in holders) cacheInvalidator.InvalidatePermissions(userId);
            }
            foreach (var pair in group) db.RolePermissionDefaults.Add(new RolePermissionDefault(role.Id, pair.PermissionCode, now));
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        await cacheInvalidator.FlushAsync(ct);
        logger.LogInformation("Applied {Count} default role permissions", pending.Count);
    }
}

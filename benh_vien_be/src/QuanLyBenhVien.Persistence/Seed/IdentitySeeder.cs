using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace QuanLyBenhVien.Persistence.Seed;

internal sealed class IdentitySeeder
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly SeedOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<IdentitySeeder> _logger;

    private readonly ICacheInvalidator _cacheInvalidator;

    public IdentitySeeder(AppDbContext db, IPasswordHasher passwordHasher, IOptions<SeedOptions> options,
        TimeProvider time, ILogger<IdentitySeeder> logger, ICacheInvalidator cacheInvalidator)
    {
        _cacheInvalidator = cacheInvalidator;
        _db = db;
        _passwordHasher = passwordHasher;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken ct = default)
    {
        await SyncPermissionCatalogAsync(ct);
        var adminRole = await EnsureSystemRolesAsync(ct);
        await EnsureFirstAdminAsync(adminRole, ct);
    }

    private async Task SyncPermissionCatalogAsync(CancellationToken ct)
    {
        var existing = await _db.Permissions.ToDictionaryAsync(p => p.Id, ct);
        foreach (var definition in Permissions.All)
        {
            if (existing.TryGetValue(definition.Code, out var permission)) permission.Update(definition);
            else _db.Permissions.Add(Permission.Create(definition));
        }

        foreach (var obsolete in existing.Keys.Where(code => !Permissions.IsDefined(code)))
            _logger.LogWarning("Permission {PermissionCode} exists in the database but not in the code catalog", obsolete);

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Role> EnsureSystemRolesAsync(CancellationToken ct)
    {
        var roles = await _db.Roles.Include(r => r.GrantedPermissions).ToDictionaryAsync(r => r.Code, ct);
        foreach (var (code, name) in SystemRoles.All)
        {
            if (roles.ContainsKey(code)) continue;
            var role = Role.Create(code, name, isSystem: true);
            _db.Roles.Add(role);
            roles[code] = role;
        }

        var admin = roles[SystemRoles.Admin];
        var before = admin.GrantedPermissions.Select(p => p.PermissionCode).ToHashSet(StringComparer.Ordinal);
        admin.SetPermissions(before.Union(Permissions.IdentityAccess.Select(p => p.Code)));
        var changed = !before.SetEquals(admin.GrantedPermissions.Select(p => p.PermissionCode));

        if (changed)
        {
            // Cache quyền `perm:{userId}` không có TTL: đổi tập quyền của vai trò phải xoá cache của mọi người đang giữ vai trò,
            // ghi cùng SaveChanges với thay đổi quyền (xoá thật ở FlushAsync sau commit, hoặc worker nếu flush lỗi).
            var holders = await _db.Set<UserRole>().Where(r => r.RoleId == admin.Id).Select(r => r.UserId).ToListAsync(ct);
            foreach (var userId in holders)
                _cacheInvalidator.InvalidatePermissions(userId);
        }

        await _db.SaveChangesAsync(ct);
        if (changed) await _cacheInvalidator.FlushAsync(ct);
        return admin;
    }

    private async Task EnsureFirstAdminAsync(Role adminRole, CancellationToken ct)
    {
        if (await _db.Set<UserRole>().AnyAsync(r => r.RoleId == adminRole.Id, ct)) return;

        if (string.IsNullOrWhiteSpace(_options.AdminEmail) || string.IsNullOrWhiteSpace(_options.AdminPassword))
        {
            _logger.LogWarning("No admin user exists and Seed:AdminEmail / Seed:AdminPassword are not configured");
            return;
        }

        var admin = User.Create(_options.AdminFullName, _options.AdminEmail, _passwordHasher.Hash(_options.AdminPassword), null);
        admin.SetRoles([adminRole.Id], assignedBy: null, _time.GetUtcNow());
        _db.Users.Add(admin);
        await _db.SaveChangesAsync(ct);
        _logger.LogInformation("Seeded first admin user {UserId}", admin.Id);
    }
}

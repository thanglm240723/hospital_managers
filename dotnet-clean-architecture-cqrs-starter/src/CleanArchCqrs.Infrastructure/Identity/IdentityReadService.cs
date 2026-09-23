using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Models;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Identity;

public sealed class IdentityReadService : IIdentityReadService
{
    private readonly AppDbContext _db;

    public IdentityReadService(AppDbContext db) => _db = db;

    public async Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.FullName, u.AvatarUrl })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;

        var access = await EffectivePermissionsQuery.LoadAsync(_db, userId, ct);
        return new MeDto(user.Id, user.Email, user.FullName, user.AvatarUrl,
            await LoadRoleRefsAsync(userId, ct),
            access!.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            access.MustChangePassword);
    }

    public async Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionFamilyId,
        DateTimeOffset now, CancellationToken ct = default)
        => await _db.SessionFamilies.AsNoTracking()
            .Where(f => f.UserId == userId && f.Status == SessionStatus.Active && f.AbsoluteExpiresAtUtc > now)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new SessionDto(f.Id, f.CreatedAtUtc, f.LastRefreshedAtUtc, f.AbsoluteExpiresAtUtc,
                f.IpAddress, f.UserAgent, f.Id == currentSessionFamilyId))
            .ToListAsync(ct);

    public async Task<PagedResult<UserSummaryDto>> GetUsersAsync(int pageNumber, int pageSize, string? searchTerm,
        CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.FullName, pattern));
        }

        var total = await query.CountAsync(ct);   // lọc trước, đếm sau (Đặc tả kỹ thuật §3.2)
        var rows = await query
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive, u.LastLoginAt })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var roleCodes = await (
                from userRole in _db.Set<UserRole>()
                join role in _db.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId)
                select new { userRole.UserId, role.Code })
            .ToListAsync(ct);

        var items = rows.Select(r => new UserSummaryDto(r.Id, r.Email, r.FullName, r.IsActive,
                roleCodes.Where(c => c.UserId == r.Id).Select(c => c.Code).Order(StringComparer.Ordinal).ToList(),
                r.LastLoginAt))
            .ToList();
        return new PagedResult<UserSummaryDto>(items, pageNumber, pageSize, total);
    }

    public async Task<UserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.FullName, u.AvatarUrl, u.IsActive, u.MustChangePassword, u.CreatedAt, u.LastLoginAt })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;

        var grants = await _db.Set<UserPermission>().AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.PermissionCode)
            .Select(p => new PermissionGrantDto(p.PermissionCode, p.Reason, p.GrantedAtUtc))
            .ToListAsync(ct);

        return new UserDetailDto(user.Id, user.Email, user.FullName, user.AvatarUrl, user.IsActive, user.MustChangePassword,
            await LoadRoleRefsAsync(userId, ct), grants, user.CreatedAt, user.LastLoginAt);
    }

    private async Task<IReadOnlyList<RoleRefDto>> LoadRoleRefsAsync(Guid userId, CancellationToken ct)
        => await (
                from userRole in _db.Set<UserRole>()
                join role in _db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                orderby role.Code
                select new RoleRefDto(role.Id, role.Code, role.Name))
            .ToListAsync(ct);
}

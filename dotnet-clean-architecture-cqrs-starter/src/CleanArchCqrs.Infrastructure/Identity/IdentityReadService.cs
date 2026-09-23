using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Interfaces;
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

    private async Task<IReadOnlyList<RoleRefDto>> LoadRoleRefsAsync(Guid userId, CancellationToken ct)
        => await (
                from userRole in _db.Set<UserRole>()
                join role in _db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                orderby role.Code
                select new RoleRefDto(role.Id, role.Code, role.Name))
            .ToListAsync(ct);
}

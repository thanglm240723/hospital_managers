using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.GetMe;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

internal sealed class AuthReadService : IAuthReadService
{
    private readonly AppDbContext _db;

    public AuthReadService(AppDbContext db) => _db = db;

    public async Task<SessionStateDto?> GetSessionStateAsync(Guid sessionFamilyId, CancellationToken ct = default)
    {
        var row = await _db.SessionFamilies.AsNoTracking()
            .Where(f => f.Id == sessionFamilyId)
            .Join(_db.Users, f => f.UserId, u => u.Id,
                (f, u) => new { f.Status, f.AbsoluteExpiresAtUtc, UserId = u.Id, u.IsActive, u.SecurityVersion })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : new SessionStateDto(row.UserId, row.Status, row.AbsoluteExpiresAtUtc, row.IsActive, row.SecurityVersion);
    }

    public async Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.FullName, u.AvatarUrl })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;

        var access = await EffectivePermissions.LoadAsync(_db, userId, ct);
        return new MeDto(user.Id, user.Email, user.FullName, user.AvatarUrl,
            await LoadRoleRefsAsync(userId, ct),
            access!.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            access.MustChangePassword);
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

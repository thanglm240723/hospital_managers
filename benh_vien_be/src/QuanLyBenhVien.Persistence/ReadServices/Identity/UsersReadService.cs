using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Common.Models;
using QuanLyBenhVien.Application.Features.Users.Common;
using QuanLyBenhVien.Application.Features.Users.ListUsers;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

internal sealed class UsersReadService(AppDbContext db) : IUsersReadService
{
    public async Task<PagedResult<UserSummaryDto>> ListAsync(ListUsersQuery query, CancellationToken ct)
    {
        var users = db.Users.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(query.SearchTerm))
        {
            var pattern = $"%{EscapeLike(query.SearchTerm.Trim())}%";
            users = users.Where(u => EF.Functions.ILike(u.Email, pattern, "\\") || EF.Functions.ILike(u.FullName, pattern, "\\"));
        }
        if (query.RoleId is { } roleId)
            users = users.Where(u => u.RoleAssignments.Any(r => r.RoleId == roleId));
        users = query.Status switch
        {
            UserStatuses.Active => users.Where(u => u.IsActive && !u.MustChangePassword),
            UserStatuses.MustChangePassword => users.Where(u => u.IsActive && u.MustChangePassword),
            UserStatuses.Locked => users.Where(u => !u.IsActive),
            _ => users,
        };

        var total = await users.CountAsync(ct);
        var rows = await users
            .OrderBy(u => u.FullName).ThenBy(u => u.Id)
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize)
            .Select(u => new
            {
                u.Id, u.Email, u.FullName, u.IsActive, u.MustChangePassword, u.RowVersion,
                Roles = (from ur in u.RoleAssignments
                         join r in db.Roles on ur.RoleId equals r.Id
                         orderby r.Code
                         select new UserRoleDto(r.Id, r.Code, r.Name)).ToList(),
            })
            .ToListAsync(ct);

        var items = rows
            .Select(r => new UserSummaryDto(r.Id, r.Email, r.FullName, r.IsActive, r.MustChangePassword, r.Roles, r.RowVersion))
            .ToList();
        return PagedResult<UserSummaryDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public async Task<UserDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id, u.Email, u.FullName, u.IsActive, u.MustChangePassword, u.RowVersion,
                Roles = (from ur in u.RoleAssignments
                         join r in db.Roles on ur.RoleId equals r.Id
                         orderby r.Code
                         select new UserRoleDto(r.Id, r.Code, r.Name)).ToList(),
                Grants = u.PermissionGrants.OrderBy(p => p.PermissionCode)
                    .Select(p => new UserPermissionGrantDto(p.PermissionCode, p.Reason)).ToList(),
            })
            .SingleOrDefaultAsync(ct);
        if (row is null) return null;

        var access = await EffectivePermissions.LoadAsync(db, id, ct);
        var effective = (access?.Permissions ?? new HashSet<string>()).Order(StringComparer.Ordinal).ToList();
        return new UserDetailDto(row.Id, row.Email, row.FullName, row.IsActive, row.MustChangePassword,
            row.Roles, row.Grants, effective, row.RowVersion);
    }

    private static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}

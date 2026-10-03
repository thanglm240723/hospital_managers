using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Features.Roles.Common;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

internal sealed class RolesReadService(AppDbContext db) : IRolesReadService
{
    public async Task<IReadOnlyList<RoleDto>> ListAsync(CancellationToken ct)
    {
        var rows = await db.Roles.AsNoTracking()
            .OrderBy(r => r.Code)
            .Select(r => new
            {
                r.Id,
                r.Code,
                r.Name,
                r.IsSystem,
                Permissions = r.GrantedPermissions.Select(p => p.PermissionCode).ToList(),
                r.RowVersion,
            })
            .ToListAsync(ct);

        return rows
            .Select(r => new RoleDto(r.Id, r.Code, r.Name, r.IsSystem, r.Permissions.Order(StringComparer.Ordinal).ToList(), r.RowVersion))
            .ToList();
    }
}

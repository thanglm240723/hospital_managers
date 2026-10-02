using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Features.Permissions.Common;
using QuanLyBenhVien.Application.Features.Permissions.ListPermissions;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

internal sealed class PermissionsReadService(AppDbContext db) : IPermissionsReadService
{
    public async Task<IReadOnlyList<PermissionDto>> ListAsync(CancellationToken ct)
        => await db.Permissions.AsNoTracking()
            .OrderBy(p => p.Group).ThenBy(p => p.Id)
            .Select(p => new PermissionDto(p.Id, p.Group, p.Description))
            .ToListAsync(ct);
}

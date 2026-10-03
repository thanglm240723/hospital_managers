using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Features.Facilities.Common;
using QuanLyBenhVien.Application.Features.StaffProfiles.Common;

namespace QuanLyBenhVien.Persistence.ReadServices.Identity;

internal sealed class StaffProfilesReadService(AppDbContext db) : IStaffProfilesReadService
{
    public async Task<StaffProfileDto?> GetByUserIdAsync(Guid userId, CancellationToken ct)
    {
        var profile = await db.StaffProfiles.AsNoTracking().Include(p => p.WorkScopes)
            .SingleOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null) return null;

        var departmentIds = profile.WorkScopes.Select(s => s.DepartmentId).ToList();
        var departments = await db.Departments.AsNoTracking().Where(d => departmentIds.Contains(d.Id)).ToListAsync(ct);
        var branchIds = profile.WorkScopes.Select(s => s.BranchId).Distinct().ToList();
        var branches = await db.Branches.AsNoTracking().Where(b => branchIds.Contains(b.Id)).ToDictionaryAsync(b => b.Id, ct);
        var departmentById = departments.ToDictionary(d => d.Id);

        var scopes = profile.WorkScopes
            .Select(s => new StaffWorkScopeDto(s.BranchId, branches[s.BranchId].Name, s.DepartmentId,
                departmentById[s.DepartmentId].Name, FacilitiesErrors.KindToString(departmentById[s.DepartmentId].Kind)))
            .OrderBy(s => s.BranchName, StringComparer.Ordinal).ThenBy(s => s.DepartmentName, StringComparer.Ordinal)
            .ToList();

        return new StaffProfileDto(profile.Id, profile.UserId, profile.StaffCode, profile.IsActive, profile.RowVersion, scopes);
    }
}

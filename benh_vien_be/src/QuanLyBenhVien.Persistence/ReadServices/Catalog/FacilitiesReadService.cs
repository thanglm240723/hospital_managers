using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Features.Facilities.Common;

namespace QuanLyBenhVien.Persistence.ReadServices.Catalog;

internal sealed class FacilitiesReadService(AppDbContext db) : IFacilitiesReadService
{
    public async Task<IReadOnlyList<BranchDto>> GetTreeAsync(CancellationToken ct)
    {
        var branches = await db.Branches.AsNoTracking().ToListAsync(ct);
        var departments = await db.Departments.AsNoTracking().ToListAsync(ct);
        var rooms = await db.Rooms.AsNoTracking().ToListAsync(ct);

        // Sắp theo Code (Ordinal) trong bộ nhớ để không phụ thuộc collation của PostgreSQL.
        var roomsByDepartment = rooms.OrderBy(r => r.Code, StringComparer.Ordinal).ToLookup(r => r.DepartmentId);
        var departmentsByBranch = departments.OrderBy(d => d.Code, StringComparer.Ordinal).ToLookup(d => d.BranchId);

        return branches.OrderBy(b => b.Code, StringComparer.Ordinal)
            .Select(b => new BranchDto(b.Id, b.Code, b.Name, b.IsActive, b.RowVersion,
                departmentsByBranch[b.Id].Select(d => new DepartmentDto(d.Id, d.Code, d.Name, FacilitiesErrors.KindToString(d.Kind),
                    d.IsActive, d.RowVersion, roomsByDepartment[d.Id].Select(FacilitiesErrors.ToDto).ToList())).ToList()))
            .ToList();
    }
}

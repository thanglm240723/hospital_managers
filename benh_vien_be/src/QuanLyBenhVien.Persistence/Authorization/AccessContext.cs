using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Application.Common.Identity;

namespace QuanLyBenhVien.Persistence.Authorization;

internal sealed class AccessContext : IAccessContext
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private AccessScope? _scope;

    public AccessContext(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<AccessScope> GetAsync(CancellationToken ct)
    {
        if (_scope is not null) return _scope;

        var userId = _currentUser.UserId
            ?? throw new InvalidOperationException("Resource-scoped access requires an authenticated user.");

        var profileId = await _db.StaffProfiles.AsNoTracking()
            .Where(p => p.UserId == userId && p.IsActive)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(ct);

        var branches = new HashSet<Guid>();
        var departments = new HashSet<Guid>();
        if (profileId is not null)
        {
            var rows = await (
                from s in _db.Set<QuanLyBenhVien.Domain.Identity.Staff.StaffWorkScope>().AsNoTracking()
                join d in _db.Departments.AsNoTracking() on s.DepartmentId equals d.Id
                join b in _db.Branches.AsNoTracking() on d.BranchId equals b.Id
                where s.StaffProfileId == profileId && d.IsActive && b.IsActive
                select new { DepartmentId = d.Id, BranchId = b.Id }).ToListAsync(ct);
            foreach (var r in rows)
            {
                departments.Add(r.DepartmentId);
                branches.Add(r.BranchId);
            }
        }

        return _scope = new AccessScope(userId, profileId, branches, departments);
    }
}

using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Domain.Identity.Staff;

namespace QuanLyBenhVien.Persistence.Repositories.Identity;

internal sealed class StaffProfileRepository : IStaffProfileRepository
{
    private readonly AppDbContext _context;

    public StaffProfileRepository(AppDbContext context) => _context = context;

    public async Task<StaffProfile?> GetByUserIdAsync(Guid userId, CancellationToken ct = default)
        => await _context.StaffProfiles.Include(p => p.WorkScopes).SingleOrDefaultAsync(p => p.UserId == userId, ct);

    public async Task AddAsync(StaffProfile profile, CancellationToken ct = default)
        => await _context.StaffProfiles.AddAsync(profile, ct);

    public void MarkChanged(StaffProfile profile) => _context.Entry(profile).Property(p => p.StaffCode).IsModified = true;
}

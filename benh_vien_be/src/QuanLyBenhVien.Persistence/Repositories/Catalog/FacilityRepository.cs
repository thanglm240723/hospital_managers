using QuanLyBenhVien.Domain.Catalog.Facilities;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Persistence.Repositories.Catalog;

internal sealed class FacilityRepository : IFacilityRepository
{
    private readonly AppDbContext _context;

    public FacilityRepository(AppDbContext context) => _context = context;

    public async Task<Branch?> GetBranchAsync(Guid id, CancellationToken ct = default)
        => await _context.Branches.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Department?> GetDepartmentAsync(Guid id, CancellationToken ct = default)
        => await _context.Departments.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<Room?> GetRoomAsync(Guid id, CancellationToken ct = default)
        => await _context.Rooms.SingleOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<Department>> GetDepartmentsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await _context.Departments.Where(x => ids.Contains(x.Id)).ToListAsync(ct);

    public async Task<Branch?> GetBranchForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        await LockAsync("Branches", id, ct);
        return await GetBranchAsync(id, ct);
    }

    public async Task<Department?> GetDepartmentForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        await LockAsync("Departments", id, ct);
        return await GetDepartmentAsync(id, ct);
    }

    public async Task<Room?> GetRoomForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        await LockAsync("Rooms", id, ct);
        return await GetRoomAsync(id, ct);
    }

    public async Task<bool> HasActiveDepartmentsAsync(Guid branchId, CancellationToken ct = default)
        => await _context.Departments.AnyAsync(x => x.BranchId == branchId && x.IsActive, ct);

    public async Task<bool> HasActiveRoomsAsync(Guid departmentId, CancellationToken ct = default)
        => await _context.Rooms.AnyAsync(x => x.DepartmentId == departmentId && x.IsActive, ct);

    private async Task LockAsync(string table, Guid id, CancellationToken ct)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks require an open transaction (IUnitOfWork.BeginTransactionAsync).");
        // Tên bảng là hằng nội bộ, không nhận từ người dùng.
        await _context.Database.ExecuteSqlRawAsync($"SELECT 1 FROM \"{table}\" WHERE \"Id\" = {{0}} FOR UPDATE", [id], ct);
    }

    public async Task AddAsync(Branch branch, CancellationToken ct = default) => await _context.Branches.AddAsync(branch, ct);

    public async Task AddAsync(Department department, CancellationToken ct = default) => await _context.Departments.AddAsync(department, ct);

    public async Task AddAsync(Room room, CancellationToken ct = default) => await _context.Rooms.AddAsync(room, ct);
}

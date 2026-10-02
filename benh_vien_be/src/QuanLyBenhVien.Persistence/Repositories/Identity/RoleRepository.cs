using QuanLyBenhVien.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Persistence.Repositories.Identity;

internal sealed class RoleRepository : IRoleRepository
{
    private readonly AppDbContext _context;

    public RoleRepository(AppDbContext context) => _context = context;

    public async Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await _context.Roles.Include(r => r.GrantedPermissions).SingleOrDefaultAsync(r => r.Id == id, ct);

    public async Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await _context.Roles.Include(r => r.GrantedPermissions).SingleOrDefaultAsync(r => r.Code == code, ct);

    public async Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
        => await _context.Roles.Where(r => ids.Contains(r.Id)).ToListAsync(ct);

    public async Task<bool> CodeExistsAsync(string code, CancellationToken ct = default)
        => await _context.Roles.AnyAsync(r => r.Code == code, ct);

    public async Task<Role?> GetForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks require an open transaction (IUnitOfWork.BeginTransactionAsync).");
        await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Roles\" WHERE \"Id\" = {id} FOR UPDATE", ct);
        return await _context.Roles.Include(r => r.GrantedPermissions).SingleOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<Guid>> LockAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks require an open transaction (IUnitOfWork.BeginTransactionAsync).");
        if (ids.Count == 0) return [];
        var array = ids.Distinct().ToArray();
        return await _context.Database
            .SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"Roles\" WHERE \"Id\" = ANY({array}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(ct);
    }

    public void MarkChanged(Role role) => _context.Entry(role).Property(r => r.Name).IsModified = true;

    public async Task AddAsync(Role role, CancellationToken ct = default)
        => await _context.Roles.AddAsync(role, ct);
}

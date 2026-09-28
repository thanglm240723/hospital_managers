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

    public async Task AddAsync(Role role, CancellationToken ct = default)
        => await _context.Roles.AddAsync(role, ct);
}

using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.Persistence.Repositories.Identity;

internal sealed class UserRepository : IUserRepository
{
    /// Khoá cố định cho pg_advisory_xact_lock — cùng một khoá cho mọi thao tác đụng vào bất biến
    /// "còn ≥ 1 admin đang hoạt động" (khoá/mở khoá tài khoản, gỡ role admin ở task 4.4/4.5...).
    private const long AdminSafetyLockKey = 7_402_198_351;

    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        return await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Email == normalized, ct);
    }

    public async Task<User?> GetForUpdateAsync(Guid id, CancellationToken ct = default)
    {
        EnsureTransaction();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM \"Users\" WHERE \"Id\" = {id} FOR UPDATE", ct);

        return await _context.Users.SingleOrDefaultAsync(u => u.Id == id, ct);
    }

    private void EnsureTransaction()
    {
        if (_context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Row locks require an open transaction (IUnitOfWork.BeginTransactionAsync).");
    }

    public async Task<User> GetUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        return user ?? throw new NotFoundException($"User with id '{id}' was not found.");
    }

    public async Task AddUserAsync(User user, CancellationToken ct = default)
        => await _context.Users.AddAsync(user, ct);

    public void UpdateUser(User user) => _context.Users.Update(user);

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        return await _context.Users.AnyAsync(u => u.Email == normalized, ct);
    }

    public async Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default)
        => await _context.Users
            .Include(u => u.RoleAssignments)
            .Include(u => u.PermissionGrants)
            .SingleOrDefaultAsync(u => u.Id == id, ct);

    public async Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default)
        => await _context.Users.CountAsync(u =>
            u.IsActive && u.Id != excludingUserId && u.RoleAssignments.Any(r => r.RoleId == roleId), ct);

    public async Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default)
        => await _context.Set<UserRole>().Where(r => r.RoleId == roleId).Select(r => r.UserId).ToListAsync(ct);

    public async Task AcquireAdminSafetyLockAsync(CancellationToken ct = default)
        => await _context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdminSafetyLockKey})", ct);
}

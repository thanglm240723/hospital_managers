using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
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
}

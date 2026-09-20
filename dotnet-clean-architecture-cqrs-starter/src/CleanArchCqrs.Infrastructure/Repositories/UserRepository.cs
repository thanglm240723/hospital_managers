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
}

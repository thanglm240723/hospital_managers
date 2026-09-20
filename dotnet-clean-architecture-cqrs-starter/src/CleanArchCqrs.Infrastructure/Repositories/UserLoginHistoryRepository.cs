using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;

namespace CleanArchCqrs.Infrastructure.Repositories;

public sealed class UserLoginHistoryRepository : IUserLoginHistoryRepository
{
    private readonly AppDbContext _context;

    public UserLoginHistoryRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(UserLoginHistory record, CancellationToken ct = default)
        => await _context.UserLoginHistories.AddAsync(record, ct);
}

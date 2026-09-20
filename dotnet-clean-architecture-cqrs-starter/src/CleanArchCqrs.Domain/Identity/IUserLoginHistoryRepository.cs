namespace CleanArchCqrs.Domain.Identity;

public interface IUserLoginHistoryRepository
{
    Task AddAsync(UserLoginHistory record, CancellationToken ct = default);
}

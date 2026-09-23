namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ILoginRateLimiter
{
    /// null nếu email chưa bị chặn.
    Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, CancellationToken ct = default);
    Task RegisterFailureAsync(string normalizedEmail, CancellationToken ct = default);
    Task ResetAsync(string normalizedEmail, CancellationToken ct = default);
}

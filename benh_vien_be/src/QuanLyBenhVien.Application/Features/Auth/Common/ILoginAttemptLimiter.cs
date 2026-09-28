namespace QuanLyBenhVien.Application.Features.Auth.Common;

public interface ILoginAttemptLimiter
{
    /// Null khi chưa bị khoá.
    Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, CancellationToken ct = default);

    Task RegisterFailureAsync(string normalizedEmail, CancellationToken ct = default);

    Task ResetAsync(string normalizedEmail, CancellationToken ct = default);
}

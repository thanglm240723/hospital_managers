using CleanArchCqrs.Domain.Common;

namespace CleanArchCqrs.Domain.Identity;

public sealed class UserLoginHistory : Entity<Guid>
{
    public Guid? UserId { get; private set; }
    public string EmailAttempted { get; private set; } = default!;
    public bool Success { get; private set; }
    public string? FailureReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }

    private UserLoginHistory() { }

    /// Tạo 1 dòng cho lần thử THẤT BẠI — luôn kèm FailureReason.
    public static UserLoginHistory Failed(Guid? userId, string emailAttempted,
        string failureReason, string? ipAddress, string? userAgent)
    {
        return new UserLoginHistory
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            EmailAttempted = emailAttempted,
            Success = false,
            FailureReason = failureReason,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AttemptedAt = DateTimeOffset.UtcNow
        };
    }

    /// Tạo 1 dòng cho lần đăng nhập THÀNH CÔNG — luôn có UserId, không có FailureReason.
    public static UserLoginHistory Succeeded(Guid userId, string emailAttempted,
        string? ipAddress, string? userAgent)
    {
        return new UserLoginHistory
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            EmailAttempted = emailAttempted,
            Success = true,
            FailureReason = null,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AttemptedAt = DateTimeOffset.UtcNow
        };
    }
}

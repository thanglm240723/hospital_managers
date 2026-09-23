namespace CleanArchCqrs.Application.Auth.Models;

public sealed record SessionValidationResult(bool IsValid, Guid? UserId, DateTimeOffset? AbsoluteExpiresAtUtc)
{
    public static SessionValidationResult Invalid { get; } = new(false, null, null);
}

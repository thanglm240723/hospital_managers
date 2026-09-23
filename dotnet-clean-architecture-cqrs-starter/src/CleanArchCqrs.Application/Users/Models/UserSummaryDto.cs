namespace CleanArchCqrs.Application.Users.Models;

public sealed record UserSummaryDto(Guid Id, string Email, string FullName, bool IsActive,
    IReadOnlyList<string> RoleCodes, DateTimeOffset? LastLoginAt);

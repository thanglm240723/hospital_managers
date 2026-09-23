namespace CleanArchCqrs.Application.Auth.Models;

public sealed record SessionDto(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastRefreshedAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    string? IpAddress,
    string? UserAgent,
    bool IsCurrent);

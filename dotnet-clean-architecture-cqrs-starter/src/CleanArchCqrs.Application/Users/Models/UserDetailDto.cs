using CleanArchCqrs.Application.Auth.Models;

namespace CleanArchCqrs.Application.Users.Models;

public sealed record UserDetailDto(
    Guid Id,
    string Email,
    string FullName,
    string? AvatarUrl,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<RoleRefDto> Roles,
    IReadOnlyList<PermissionGrantDto> PermissionGrants,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);

namespace CleanArchCqrs.Application.Auth.Models;

/// Không bao giờ chứa PasswordHash — map thủ công từng field.
public sealed record MeDto(
    Guid Id,
    string Email,
    string FullName,
    string? AvatarUrl,
    IReadOnlyList<RoleRefDto> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

namespace QuanLyBenhVien.Application.Features.Users.Common;

public sealed record UserPermissionGrantDto(string Code, string Reason);

/// Không chứa PasswordHash, SecurityVersion hay refresh token.
public sealed record UserDetailDto(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<UserRoleDto> Roles,
    IReadOnlyList<UserPermissionGrantDto> PermissionGrants,
    IReadOnlyList<string> EffectivePermissions,
    uint RowVersion);

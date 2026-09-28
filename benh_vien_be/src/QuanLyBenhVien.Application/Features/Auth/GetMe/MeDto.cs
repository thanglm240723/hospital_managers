namespace QuanLyBenhVien.Application.Features.Auth.GetMe;

public sealed record MeDto(
    Guid Id,
    string Email,
    string FullName,
    string? AvatarUrl,
    IReadOnlyList<RoleRefDto> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);

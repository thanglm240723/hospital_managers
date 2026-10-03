namespace QuanLyBenhVien.Application.Features.Users.Common;

public sealed record UserRoleDto(Guid Id, string Code, string Name);

public sealed record UserSummaryDto(
    Guid Id,
    string Email,
    string FullName,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<UserRoleDto> Roles,
    uint RowVersion);

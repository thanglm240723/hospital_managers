namespace QuanLyBenhVien.Application.Features.Roles.Common;

public sealed record RoleDto(
    Guid Id,
    string Code,
    string Name,
    bool IsSystem,
    IReadOnlyList<string> PermissionCodes,
    uint RowVersion);

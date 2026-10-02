using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;

namespace QuanLyBenhVien.Application.Features.Roles.SetRolePermissions;

public sealed record SetRolePermissionsCommand(Guid Id, IReadOnlyList<string> PermissionCodes, uint ExpectedVersion) : ICommand<Result<RoleDto>>;

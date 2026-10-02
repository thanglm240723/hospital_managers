using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;

namespace QuanLyBenhVien.Application.Features.Roles.CreateRole;

public sealed record CreateRoleCommand(string Code, string Name, IReadOnlyList<string> PermissionCodes) : ICommand<Result<RoleDto>>;

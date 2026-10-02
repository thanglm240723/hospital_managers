using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Roles.Common;

namespace QuanLyBenhVien.Application.Features.Roles.RenameRole;

public sealed record RenameRoleCommand(Guid Id, string Name, uint ExpectedVersion) : ICommand<Result<RoleDto>>;

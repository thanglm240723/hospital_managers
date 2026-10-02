using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.SetUserRoles;

public sealed record SetUserRolesCommand(Guid Id, IReadOnlyList<Guid> RoleIds, uint ExpectedVersion) : ICommand<Result<UserDetailDto>>;

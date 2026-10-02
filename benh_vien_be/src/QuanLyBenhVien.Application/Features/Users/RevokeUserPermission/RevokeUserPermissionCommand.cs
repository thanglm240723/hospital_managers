using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.RevokeUserPermission;

public sealed record RevokeUserPermissionCommand(Guid Id, string PermissionCode, string Reason, uint ExpectedVersion) : ICommand<Result<UserDetailDto>>;

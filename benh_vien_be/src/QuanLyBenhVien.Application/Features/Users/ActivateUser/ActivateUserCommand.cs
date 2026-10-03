using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.ActivateUser;

public sealed record ActivateUserCommand(Guid Id) : ICommand<Result<UserDetailDto>>;

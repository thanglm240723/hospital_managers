using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.DeactivateUser;

public sealed record DeactivateUserCommand(Guid Id) : ICommand<Result<UserDetailDto>>;

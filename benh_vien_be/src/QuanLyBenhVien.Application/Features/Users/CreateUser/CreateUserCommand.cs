using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Users.CreateUser;

public sealed record CreateUserCommand(string Email, string FullName, IReadOnlyList<Guid> RoleIds) : ICommand<Result<CreateUserResultDto>>;

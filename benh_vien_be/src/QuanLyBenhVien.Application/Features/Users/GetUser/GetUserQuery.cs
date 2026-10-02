using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.GetUser;

public sealed record GetUserQuery(Guid Id) : IQuery<Result<UserDetailDto>>;

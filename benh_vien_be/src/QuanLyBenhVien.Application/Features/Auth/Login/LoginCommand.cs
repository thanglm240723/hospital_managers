using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Application.Features.Auth.Login;

public sealed record LoginCommand(string Email, string Password) : ICommand<Result<AuthTokensResult>>;

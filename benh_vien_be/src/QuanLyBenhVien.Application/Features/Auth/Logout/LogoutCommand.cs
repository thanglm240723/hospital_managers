using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.Logout;

public sealed record LogoutCommand(string? RefreshToken) : ICommand<Result>;

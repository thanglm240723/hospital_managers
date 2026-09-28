using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.LogoutAll;

public sealed record LogoutAllCommand() : ICommand<Result>;

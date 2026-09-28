using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;

namespace QuanLyBenhVien.Application.Features.Auth.RefreshSession;

public sealed record RefreshSessionCommand(string RefreshToken) : ICommand<Result<AuthTokensResult>>;

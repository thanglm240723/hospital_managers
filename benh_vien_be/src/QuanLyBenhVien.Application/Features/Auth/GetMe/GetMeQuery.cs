using QuanLyBenhVien.Application.Common.Messaging;
using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.GetMe;

public sealed record GetMeQuery() : IQuery<Result<MeDto>>;

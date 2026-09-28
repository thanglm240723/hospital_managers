using QuanLyBenhVien.Domain.Identity.Sessions;

namespace QuanLyBenhVien.Application.Features.Auth.Common;

public sealed record SessionStateDto(
    Guid UserId,
    SessionStatus Status,
    DateTimeOffset AbsoluteExpiresAtUtc,
    bool IsActive,
    int SecurityVersion);

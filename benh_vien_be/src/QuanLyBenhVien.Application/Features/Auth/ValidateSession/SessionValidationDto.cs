namespace QuanLyBenhVien.Application.Features.Auth.ValidateSession;

/// Gateway chỉ đọc `Valid`.
public sealed record SessionValidationDto(bool Valid, Guid? UserId, DateTimeOffset? AbsoluteExpiresAtUtc);

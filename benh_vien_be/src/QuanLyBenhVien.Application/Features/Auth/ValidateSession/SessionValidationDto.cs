namespace QuanLyBenhVien.Application.Features.Auth.ValidateSession;

/// Gateway chỉ đọc `Valid`. `AbsExp` là Unix seconds theo đặc tả §3.10 (`{ valid, userId, absExp }`).
public sealed record SessionValidationDto(bool Valid, Guid? UserId, long? AbsExp);

using QuanLyBenhVien.Domain.Identity.Sessions;

namespace QuanLyBenhVien.Application.Features.Auth.Common;

/// Kết quả định vị family theo hash refresh token — đủ để quyết định CSRF/logout, không phải trạng thái phiên đầy đủ.
public sealed record RefreshSessionRef(Guid FamilyId, Guid UserId, SessionStatus Status);

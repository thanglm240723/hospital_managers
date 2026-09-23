namespace CleanArchCqrs.Application.Auth.Models;

/// Kết quả login/refresh. RefreshToken chỉ đi vào cookie HttpOnly, không bao giờ vào body response.
public sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    bool MustChangePassword,
    string RefreshToken,
    Guid SessionFamilyId,
    DateTimeOffset SessionExpiresAtUtc);

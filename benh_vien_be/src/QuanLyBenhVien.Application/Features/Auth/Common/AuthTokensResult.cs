namespace QuanLyBenhVien.Application.Features.Auth.Common;

public sealed record AuthTokensResult(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset SessionExpiresAtUtc,
    string CsrfToken,
    bool MustChangePassword);

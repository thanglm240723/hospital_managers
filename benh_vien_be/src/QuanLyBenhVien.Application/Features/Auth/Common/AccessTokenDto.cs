namespace QuanLyBenhVien.Application.Features.Auth.Common;

/// Body trả về cho FE sau đăng nhập. KHÔNG chứa refresh token (đi qua cookie __Host-rt).
public sealed record AccessTokenDto(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword);

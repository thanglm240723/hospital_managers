namespace QuanLyBenhVien.Application.Features.Auth.Common;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

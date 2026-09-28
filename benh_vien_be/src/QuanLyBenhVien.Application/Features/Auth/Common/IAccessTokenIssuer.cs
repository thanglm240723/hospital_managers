namespace QuanLyBenhVien.Application.Features.Auth.Common;

public interface IAccessTokenIssuer
{
}

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAtUtc);

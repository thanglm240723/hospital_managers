namespace QuanLyBenhVien.Application.Features.Auth.Common;

public interface IRefreshTokenGenerator
{
    GeneratedRefreshToken Generate();

    string Hash(string token);
}

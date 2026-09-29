using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Auth.Common;

public static class AuthErrors
{
    public static readonly Error InvalidCredentials =
        new("unauthenticated", "Email hoặc mật khẩu không đúng.", ErrorType.Unauthorized);

    public static readonly Error Unauthenticated =
        new("unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.", ErrorType.Unauthorized);
}

using QuanLyBenhVien.Application.Common.Results;

namespace QuanLyBenhVien.Application.Features.Users.Common;

/// Lỗi nghiệp vụ của feature Users.
public static class UsersErrors
{
    public static readonly Error NotFound =
        new("user_not_found", "Không tìm thấy tài khoản.", ErrorType.NotFound);
}

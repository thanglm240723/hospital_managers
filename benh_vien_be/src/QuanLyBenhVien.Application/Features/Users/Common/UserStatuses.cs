namespace QuanLyBenhVien.Application.Features.Users.Common;

/// Giá trị hợp lệ của bộ lọc trạng thái tài khoản.
public static class UserStatuses
{
    public const string Active = "active";
    public const string MustChangePassword = "must_change_password";
    public const string Locked = "locked";

    public static bool IsValid(string? status) => status is Active or MustChangePassword or Locked;
}

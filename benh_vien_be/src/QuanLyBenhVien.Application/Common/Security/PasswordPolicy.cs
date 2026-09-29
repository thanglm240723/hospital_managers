namespace QuanLyBenhVien.Application.Common.Security;

/// Chính sách mật khẩu dùng chung cho đổi mật khẩu (và các nơi khác cần kiểm mật khẩu mới sau này).
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    /// So khớp không phân biệt hoa thường phần trước "@" của email trong mật khẩu.
    public static bool ContainsEmailLocalPart(string password, string email)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(email)) return false;

        var at = email.IndexOf('@');
        var localPart = at > 0 ? email[..at] : email;
        if (localPart.Length == 0) return false;

        return password.Contains(localPart, StringComparison.OrdinalIgnoreCase);
    }
}

namespace CleanArchCqrs.Application.Common.Security;

/// Theo hướng NIST: đủ dài, không ép kiểu ký tự, không đổi định kỳ.
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static bool ContainsEmailLocalPart(string password, string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return local.Length > 0 && password.Contains(local, StringComparison.OrdinalIgnoreCase);
    }
}

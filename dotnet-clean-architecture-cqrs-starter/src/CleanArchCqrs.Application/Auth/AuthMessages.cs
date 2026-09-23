namespace CleanArchCqrs.Application.Auth;

public static class AuthMessages
{
    /// Giống hệt cho email không tồn tại / sai mật khẩu / tài khoản bị khoá — chống dò email.
    public const string LoginFailed = "Email hoặc mật khẩu không đúng.";
    public const string SessionInvalid = "Phiên đăng nhập không còn hiệu lực.";
    public const string TooManyAttempts = "Bạn đã thử quá nhiều lần. Vui lòng thử lại sau.";
}

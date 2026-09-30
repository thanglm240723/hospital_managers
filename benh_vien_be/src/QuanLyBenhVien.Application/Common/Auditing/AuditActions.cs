namespace QuanLyBenhVien.Application.Common.Auditing;

public static class AuditActions
{
    public const string Login = "auth.login";
    public const string PasswordChange = "auth.password.change";
    public const string Logout = "auth.logout";
    public const string LogoutAll = "auth.logout_all";
    public const string RefreshReuse = "auth.refresh.reuse";
}

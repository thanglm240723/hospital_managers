namespace QuanLyBenhVien.Presentation.Auth;

/// Nguồn family để đối chiếu X-CSRF-Token: `Bearer` đọc claim `fid` của access token (route cần đăng nhập),
/// `RefreshCookie` đọc cookie `__Host-rt` rồi định vị family qua `IRefreshSessionLookup` (route công khai như logout).
public enum CsrfTokenSource
{
    Bearer,
    RefreshCookie,
}

/// Đánh dấu route cần Origin trong allowlist + header X-CSRF-Token khớp family xác định theo `Source`.
public sealed class CsrfProtectionMetadata
{
    public static readonly CsrfProtectionMetadata Bearer = new(CsrfTokenSource.Bearer);
    public static readonly CsrfProtectionMetadata RefreshCookie = new(CsrfTokenSource.RefreshCookie);

    public CsrfTokenSource Source { get; }

    private CsrfProtectionMetadata(CsrfTokenSource source) => Source = source;
}

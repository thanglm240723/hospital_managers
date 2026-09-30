namespace QuanLyBenhVien.Presentation.Auth;

/// Đánh dấu route cần Origin trong allowlist + header X-CSRF-Token khớp family trong claim `fid`.
public sealed class CsrfProtectionMetadata
{
    public static readonly CsrfProtectionMetadata Instance = new();

    private CsrfProtectionMetadata()
    {
    }
}

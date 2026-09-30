namespace QuanLyBenhVien.Presentation.Auth;

/// Route vẫn dùng được khi user đang bị bắt đổi mật khẩu (me, change-password, logout, logout-all — spec cũ §3.9).
public sealed class AllowWhilePasswordChangeRequiredMetadata
{
    public static readonly AllowWhilePasswordChangeRequiredMetadata Instance = new();

    private AllowWhilePasswordChangeRequiredMetadata()
    {
    }
}

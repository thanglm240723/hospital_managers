namespace QuanLyBenhVien.Application.Common.Identity;

public interface ICurrentUser
{
    Guid? UserId { get; }
    Guid? SessionFamilyId { get; }

    /// Security version mang trong access token (claim "sv") — dùng để cấp lại token cùng phiên sau khi đổi mật khẩu.
    int? SecurityVersion { get; }
}

namespace QuanLyBenhVien.Application.Common.Identity;

/// Quyền hiệu lực và cờ truy cập của một user. User không hoạt động có Permissions rỗng.
public sealed record UserAccessDto(bool IsActive, bool MustChangePassword, IReadOnlySet<string> Permissions);

namespace QuanLyBenhVien.Presentation.Endpoints.V1.Users;

/// Dùng cho cả grant và revoke quyền lẻ.
public sealed record PermissionGrantRequest(string? PermissionCode, string? Reason);

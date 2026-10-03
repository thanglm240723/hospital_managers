namespace QuanLyBenhVien.Presentation.Endpoints.V1.Users;

/// Chỉ nhận email/họ tên/vai trò — IsActive, SecurityVersion, mật khẩu do server quyết định.
public sealed record CreateUserRequest(string? Email, string? FullName, IReadOnlyList<Guid>? RoleIds);

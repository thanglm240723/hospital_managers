namespace QuanLyBenhVien.Presentation.Endpoints.V1.Roles;

public sealed record CreateRoleRequest(string Code, string Name, IReadOnlyList<string>? PermissionCodes);

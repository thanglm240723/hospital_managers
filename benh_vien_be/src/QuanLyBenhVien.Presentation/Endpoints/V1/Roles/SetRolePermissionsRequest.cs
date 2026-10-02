namespace QuanLyBenhVien.Presentation.Endpoints.V1.Roles;

public sealed record SetRolePermissionsRequest(IReadOnlyList<string>? PermissionCodes);

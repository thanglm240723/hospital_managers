namespace QuanLyBenhVien.Presentation.Endpoints.V1.Users;

public sealed record SetUserRolesRequest(IReadOnlyList<Guid>? RoleIds);

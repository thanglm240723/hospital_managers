namespace QuanLyBenhVien.Presentation.Endpoints.V1.StaffProfiles;

public sealed record SetStaffWorkScopesRequest(IReadOnlyList<Guid> DepartmentIds);

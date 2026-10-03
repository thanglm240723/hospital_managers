namespace QuanLyBenhVien.Presentation.Endpoints.V1.Facilities;

public sealed record CreateDepartmentRequest(Guid BranchId, string Code, string Name, string Kind);

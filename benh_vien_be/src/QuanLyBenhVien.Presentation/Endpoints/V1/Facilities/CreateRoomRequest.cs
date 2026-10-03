namespace QuanLyBenhVien.Presentation.Endpoints.V1.Facilities;

public sealed record CreateRoomRequest(Guid DepartmentId, string Code, string Name);

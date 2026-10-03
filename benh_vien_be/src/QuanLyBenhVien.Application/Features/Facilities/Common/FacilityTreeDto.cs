namespace QuanLyBenhVien.Application.Features.Facilities.Common;

public sealed record RoomDto(Guid Id, string Code, string Name, bool IsActive, uint RowVersion);

public sealed record DepartmentDto(Guid Id, string Code, string Name, string Kind, bool IsActive, uint RowVersion, IReadOnlyList<RoomDto> Rooms);

public sealed record BranchDto(Guid Id, string Code, string Name, bool IsActive, uint RowVersion, IReadOnlyList<DepartmentDto> Departments);

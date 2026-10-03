namespace QuanLyBenhVien.Application.Features.StaffProfiles.Common;

public sealed record StaffWorkScopeDto(Guid BranchId, string BranchName, Guid DepartmentId, string DepartmentName, string DepartmentKind);

public sealed record StaffProfileDto(Guid Id, Guid UserId, string StaffCode, bool IsActive, uint RowVersion, IReadOnlyList<StaffWorkScopeDto> WorkScopes);

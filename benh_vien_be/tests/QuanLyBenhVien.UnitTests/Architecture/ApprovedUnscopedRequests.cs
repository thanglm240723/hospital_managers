using QuanLyBenhVien.Application.Features.Facilities.CreateBranch;
using QuanLyBenhVien.Application.Features.Facilities.CreateDepartment;
using QuanLyBenhVien.Application.Features.Facilities.CreateRoom;
using QuanLyBenhVien.Application.Features.Facilities.GetFacilityTree;
using QuanLyBenhVien.Application.Features.Facilities.UpdateBranch;
using QuanLyBenhVien.Application.Features.Facilities.UpdateDepartment;
using QuanLyBenhVien.Application.Features.Facilities.UpdateRoom;
using QuanLyBenhVien.Application.Features.StaffProfiles.GetStaffProfile;
using QuanLyBenhVien.Application.Features.StaffProfiles.SetStaffWorkScopes;
using QuanLyBenhVien.Application.Features.StaffProfiles.UpsertStaffProfile;

namespace QuanLyBenhVien.UnitTests.Architecture;

/// Thêm mục = thay đổi phải qua review; mỗi mục có lý do.
internal static class ApprovedUnscopedRequests
{
    private const string FacilitiesReason = "Danh mục cơ cấu tổ chức, không chứa PHI; quyền lớp 1 đủ";

    private const string StaffProfilesReason = "Quản trị hồ sơ nhân sự; quyền lớp 1 đủ";

    public static IReadOnlyDictionary<Type, string> All { get; } = new Dictionary<Type, string>
    {
        [typeof(GetFacilityTreeQuery)] = FacilitiesReason,
        [typeof(CreateBranchCommand)] = FacilitiesReason,
        [typeof(CreateDepartmentCommand)] = FacilitiesReason,
        [typeof(CreateRoomCommand)] = FacilitiesReason,
        [typeof(UpdateBranchCommand)] = FacilitiesReason,
        [typeof(UpdateDepartmentCommand)] = FacilitiesReason,
        [typeof(UpdateRoomCommand)] = FacilitiesReason,
        [typeof(GetStaffProfileQuery)] = StaffProfilesReason,
        [typeof(UpsertStaffProfileCommand)] = StaffProfilesReason,
        [typeof(SetStaffWorkScopesCommand)] = StaffProfilesReason,
    };
}

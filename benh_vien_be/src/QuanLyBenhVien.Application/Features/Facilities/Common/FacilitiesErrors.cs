using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Domain.Catalog.Facilities;

namespace QuanLyBenhVien.Application.Features.Facilities.Common;

/// Lỗi nghiệp vụ của feature Facilities.
public static class FacilitiesErrors
{
    public static readonly Error NotFound =
        new("facility_not_found", "Không tìm thấy cơ sở, khoa hoặc phòng.", ErrorType.NotFound);

    public static readonly Error CodeTaken =
        new("facility_code_taken", "Mã đã tồn tại trong phạm vi cha.", ErrorType.Conflict);

    public static readonly Error VersionConflict =
        new("facility_version_conflict", "Dữ liệu đã được thay đổi bởi người khác. Vui lòng tải lại.", ErrorType.Precondition);

    public static readonly Error ParentInactive =
        new("parent_facility_inactive", "Không thể tạo mục con dưới mục đã ngừng sử dụng.", ErrorType.Conflict);

    public static readonly Error HasActiveChildren =
        new("facility_has_active_children", "Không thể ngừng sử dụng khi còn mục con đang sử dụng.", ErrorType.Conflict);

    public const string BranchCodeConstraint = "IX_Branches_Code";
    public const string DepartmentCodeConstraint = "IX_Departments_BranchId_Code";
    public const string RoomCodeConstraint = "IX_Rooms_DepartmentId_Code";

    public static RoomDto ToDto(Room x) => new(x.Id, x.Code, x.Name, x.IsActive, x.RowVersion);

    public static DepartmentDto ToDto(Department x) => new(x.Id, x.Code, x.Name, KindToString(x.Kind), x.IsActive, x.RowVersion, []);

    public static BranchDto ToDto(Branch x) => new(x.Id, x.Code, x.Name, x.IsActive, x.RowVersion, []);

    public static string KindToString(DepartmentKind kind) => kind.ToString().ToLowerInvariant();

    public static bool TryParseKind(string? value, out DepartmentKind kind)
    {
        kind = default;
        return value is not null && value == value.ToLowerInvariant()
            && Enum.TryParse(value, ignoreCase: true, out kind) && Enum.IsDefined(kind) && !int.TryParse(value, out _);
    }
}

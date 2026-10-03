using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Identity.Staff;

public sealed class StaffWorkScope : IAuditable
{
    public Guid StaffProfileId { get; private set; }
    public Guid DepartmentId { get; private set; }
    public Guid BranchId { get; private set; }

    private StaffWorkScope() { }

    internal StaffWorkScope(Guid staffProfileId, Guid departmentId, Guid branchId)
    {
        StaffProfileId = staffProfileId;
        DepartmentId = departmentId;
        BranchId = branchId;
    }
}

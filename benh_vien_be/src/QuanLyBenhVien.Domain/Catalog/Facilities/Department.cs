using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Catalog.Facilities;

public sealed class Department : AggregateRoot<Guid>, IAuditable
{
    public Guid BranchId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public DepartmentKind Kind { get; private set; }
    public bool IsActive { get; private set; }
    public uint RowVersion { get; private set; }

    private Department() { }

    public static Department Create(Guid branchId, string code, string name, DepartmentKind kind)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentException("Unknown department kind.", nameof(kind));
        return new Department
        {
            Id = Guid.CreateVersion7(),
            BranchId = branchId,
            Code = FacilityRules.ValidCode(code),
            Name = FacilityRules.ValidName(name),
            Kind = kind,
            IsActive = true,
        };
    }

    public void Rename(string name) => Name = FacilityRules.ValidName(name);

    public void SetActive(bool isActive) => IsActive = isActive;
}

using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Catalog.Facilities;

public sealed class Branch : AggregateRoot<Guid>, IAuditable
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public uint RowVersion { get; private set; }

    private Branch() { }

    public static Branch Create(string code, string name)
        => new()
        {
            Id = Guid.CreateVersion7(),
            Code = FacilityRules.ValidCode(code),
            Name = FacilityRules.ValidName(name),
            IsActive = true,
        };

    public void Rename(string name) => Name = FacilityRules.ValidName(name);

    public void SetActive(bool isActive) => IsActive = isActive;
}

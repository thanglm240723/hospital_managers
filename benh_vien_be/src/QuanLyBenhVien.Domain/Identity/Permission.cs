using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity;

/// Bản ghi DB của một mục trong danh mục Permissions. Id chính là mã quyền.
public sealed class Permission : Entity<string>
{
    public string Group { get; private set; } = default!;
    public string Description { get; private set; } = default!;

    private Permission() { }

    public static Permission Create(PermissionDefinition definition)
        => new() { Id = definition.Code, Group = definition.Group, Description = definition.Description };

    public void Update(PermissionDefinition definition)
    {
        Group = definition.Group;
        Description = definition.Description;
    }
}

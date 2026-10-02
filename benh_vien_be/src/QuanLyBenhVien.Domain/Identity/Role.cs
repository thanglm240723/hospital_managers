using System.Text.RegularExpressions;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;

namespace QuanLyBenhVien.Domain.Identity;

public sealed class Role : AggregateRoot<Guid>, IAuditable
{
    private static readonly Regex CodePattern = new("^[a-z][a-z0-9-]{1,49}$", RegexOptions.Compiled);

    private readonly List<RolePermission> _grantedPermissions = new();

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public bool IsSystem { get; private set; }
    public uint RowVersion { get; private set; }
    public IReadOnlyCollection<RolePermission> GrantedPermissions => _grantedPermissions.AsReadOnly();

    private Role() { }

    public static bool IsValidCode(string code) => !string.IsNullOrEmpty(code) && CodePattern.IsMatch(code);

    public static Role Create(string code, string name, bool isSystem = false)
    {
        if (!IsValidCode(code))
            throw new ArgumentException("Role code must be lower-kebab-case, 2-50 characters.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name cannot be empty.", nameof(name));

        return new Role { Id = Guid.CreateVersion7(), Code = code, Name = name.Trim(), IsSystem = isSystem };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Role name cannot be empty.", nameof(name));
        Name = name.Trim();
    }

    /// Trả true nếu tập quyền thực sự thay đổi.
    public bool SetPermissions(IEnumerable<string> permissionCodes)
    {
        var target = permissionCodes.Distinct(StringComparer.Ordinal).ToList();
        foreach (var code in target)
            if (!Permissions.IsDefined(code))
                throw new ArgumentException($"Unknown permission '{code}'.", nameof(permissionCodes));

        var removed = _grantedPermissions.RemoveAll(p => !target.Contains(p.PermissionCode));
        var toAdd = target.Where(c => _grantedPermissions.All(p => p.PermissionCode != c)).ToList();
        foreach (var code in toAdd)
            _grantedPermissions.Add(new RolePermission(Id, code));
        return removed > 0 || toAdd.Count > 0;
    }
}

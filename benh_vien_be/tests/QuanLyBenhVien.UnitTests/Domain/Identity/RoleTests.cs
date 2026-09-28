using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Domain.Identity;

public class RoleTests
{
    [Fact]
    public void Create_ValidInput_SetsFields()
    {
        var role = Role.Create("doctor", "  Bác sĩ ", isSystem: true);

        Assert.NotEqual(Guid.Empty, role.Id);
        Assert.Equal("doctor", role.Code);
        Assert.Equal("Bác sĩ", role.Name);
        Assert.True(role.IsSystem);
        Assert.Empty(role.GrantedPermissions);
    }

    [Theory]
    [InlineData("Admin")]
    [InlineData("a")]
    [InlineData("has space")]
    [InlineData("under_score")]
    [InlineData("-leading")]
    public void Create_InvalidCode_Throws(string code)
        => Assert.Throws<ArgumentException>(() => Role.Create(code, "Tên"));

    [Fact]
    public void Create_EmptyName_Throws()
        => Assert.Throws<ArgumentException>(() => Role.Create("doctor", " "));

    [Fact]
    public void SetPermissions_ReplacesWholeSetAndIgnoresDuplicates()
    {
        var role = Role.Create("tester", "Tester");
        role.SetPermissions([Permissions.Users.Read, Permissions.Roles.Read]);

        role.SetPermissions([Permissions.Roles.Read, Permissions.Catalog.Read, Permissions.Catalog.Read]);

        Assert.Equal(
            new[] { Permissions.Catalog.Read, Permissions.Roles.Read },
            role.GrantedPermissions.Select(p => p.PermissionCode).OrderBy(c => c, StringComparer.Ordinal));
        Assert.All(role.GrantedPermissions, p => Assert.Equal(role.Id, p.RoleId));
    }

    [Fact]
    public void SetPermissions_UnknownCode_Throws()
    {
        var role = Role.Create("tester", "Tester");

        Assert.Throws<ArgumentException>(() => role.SetPermissions(["patients.read"]));
    }

    [Fact]
    public void Rename_TrimsAndRejectsEmpty()
    {
        var role = Role.Create("tester", "Tester");

        role.Rename("  Kiểm thử ");

        Assert.Equal("Kiểm thử", role.Name);
        Assert.Throws<ArgumentException>(() => role.Rename(""));
    }

    [Fact]
    public void SystemRoleCodes_AreAllValid()
        => Assert.All(SystemRoles.All, r => Assert.True(Role.IsValidCode(r.Code), r.Code));

    [Fact]
    public void RoleAndRolePermission_AreAuditable()
    {
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(Role)));
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(RolePermission)));
    }
}

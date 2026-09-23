using CleanArchCqrs.Domain.Identity;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class PermissionsTests
{
    [Fact]
    public void AllCodes_AreUniqueAndLowercaseDotSeparated()
    {
        var codes = Permissions.All.Select(p => p.Code).ToList();

        Assert.Equal(codes.Count, codes.Distinct(StringComparer.Ordinal).Count());
        Assert.All(codes, code => Assert.Matches("^[a-z]+(\\.[a-z]+)+$", code));
    }

    [Fact]
    public void IdentityAccess_HasTheEightSpecPermissions()
    {
        Assert.Equal(
            new[] { "permissions.read", "roles.manage", "roles.read", "users.activate", "users.create",
                    "users.permissions.manage", "users.read", "users.roles.manage" },
            Permissions.IdentityAccess.Select(p => p.Code).OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public void IsDefined_RecognisesOnlyCatalogCodes()
    {
        Assert.True(Permissions.IsDefined(Permissions.Users.Read));
        Assert.False(Permissions.IsDefined("patients.read"));
    }

    [Fact]
    public void Permission_CreateAndUpdate_CopyDefinition()
    {
        var permission = Permission.Create(new PermissionDefinition("users.read", "Tài khoản", "Xem"));
        permission.Update(new PermissionDefinition("users.read", "Tài khoản", "Xem danh sách"));

        Assert.Equal("users.read", permission.Id);
        Assert.Equal("Xem danh sách", permission.Description);
    }

    [Fact]
    public void SystemRoles_HasNineUniqueCodesIncludingAdmin()
    {
        var codes = SystemRoles.All.Select(r => r.Code).ToList();

        Assert.Equal(9, codes.Count);
        Assert.Equal(9, codes.Distinct().Count());
        Assert.Contains(SystemRoles.Admin, codes);
    }
}

using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Events;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Domain.Identity;

public class UserTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    private static User NewUser() => User.Create("Nguyen Van A", "A@Example.com", "hash-1", null);

    [Fact]
    public void Create_RequiresPasswordChangeAndStartsAtSecurityVersion1()
    {
        var user = NewUser();

        Assert.True(user.MustChangePassword);
        Assert.Equal(1, user.SecurityVersion);
        Assert.Equal("a@example.com", user.Email);
        Assert.IsType<UserRegisteredDomainEvent>(Assert.Single(user.DomainEvents));
    }

    [Fact]
    public void SetRoles_AddsRemovesAndOnlyRaisesWhenChanged()
    {
        var user = NewUser();
        var doctor = Guid.NewGuid();
        var cashier = Guid.NewGuid();
        user.ClearDomainEvents();

        user.SetRoles([doctor, cashier, doctor], assignedBy: null, Now);
        user.SetRoles([cashier], assignedBy: null, Now);
        user.SetRoles([cashier], assignedBy: null, Now);

        Assert.Equal(cashier, Assert.Single(user.RoleAssignments).RoleId);
        Assert.True(user.HasRole(cashier));
        Assert.False(user.HasRole(doctor));
        Assert.Equal(2, user.DomainEvents.OfType<UserRolesChangedDomainEvent>().Count());
    }

    [Fact]
    public void GrantPermission_IsIdempotentAndRequiresKnownCodeAndReason()
    {
        var user = NewUser();

        user.GrantPermission(Permissions.Users.Read, "Hỗ trợ tra cứu", grantedBy: null, Now);
        user.GrantPermission(Permissions.Users.Read, "Lần 2", grantedBy: null, Now);

        var grant = Assert.Single(user.PermissionGrants);
        Assert.Equal("Hỗ trợ tra cứu", grant.Reason);
        Assert.Throws<ArgumentException>(() => user.GrantPermission("patients.read", "x", null, Now));
        Assert.Throws<ArgumentException>(() => user.GrantPermission(Permissions.Roles.Read, " ", null, Now));
    }

    [Fact]
    public void RevokePermission_RemovesGrant()
    {
        var user = NewUser();
        user.GrantPermission(Permissions.Users.Read, "cần", null, Now);

        user.RevokePermission(Permissions.Users.Read);

        Assert.Empty(user.PermissionGrants);
    }

    [Fact]
    public void ChangePassword_BumpsSecurityVersionAndClearsMustChange()
    {
        var user = NewUser();

        user.ChangePassword("hash-2");

        Assert.Equal(2, user.SecurityVersion);
        Assert.False(user.MustChangePassword);
    }

    [Fact]
    public void Deactivate_BumpsSecurityVersionOnce()
    {
        var user = NewUser();

        user.Deactivate();
        user.Deactivate();

        Assert.False(user.IsActive);
        Assert.Equal(2, user.SecurityVersion);
    }
}

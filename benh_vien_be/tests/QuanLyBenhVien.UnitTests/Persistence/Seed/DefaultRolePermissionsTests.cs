using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence.Seed;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence.Seed;

public class DefaultRolePermissionsTests
{
    [Fact]
    public void Matrix_CoversAllSystemRoles_AndOnlyPlannedCodes()
    {
        Assert.Equal(SystemRoles.All.Select(r => r.Code).Order(), DefaultRolePermissions.ByRole.Keys.Order());
        var planned = DefaultRolePermissions.PlannedCodes.ToHashSet(StringComparer.Ordinal);
        foreach (var (_, codes) in DefaultRolePermissions.ByRole)
            Assert.All(codes, c => Assert.Contains(c, planned));
        Assert.All(DefaultRolePermissions.Supplementary, c => Assert.Contains(c, planned));
    }

    [Fact]
    public void EveryActivatedPermission_HasADefaultDecision()
    {
        var decided = DefaultRolePermissions.ByRole.Values.SelectMany(c => c)
            .Concat(DefaultRolePermissions.Supplementary).ToHashSet(StringComparer.Ordinal);
        // Mã không có vai trò mặc định nào phải nằm trong Supplementary — để không ai quên quyết định gán.
        var undecided = Permissions.All.Select(p => p.Code).Where(c => !decided.Contains(c)).ToList();
        Assert.Empty(undecided);
    }

    [Fact]
    public void EmergencyAccess_IsNotInDefaults() =>
        Assert.DoesNotContain(DefaultRolePermissions.ByRole.Values.SelectMany(c => c), c => c == "access-grants.request-emergency");

    [Fact]
    public void Admin_HasNoClinicalPermission() =>
        Assert.DoesNotContain(DefaultRolePermissions.ByRole[SystemRoles.Admin],
            c => c.StartsWith("encounters.") || c.StartsWith("patient-history.") || c.StartsWith("orders.") || c.StartsWith("results."));
}

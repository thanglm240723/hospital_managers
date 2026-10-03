using QuanLyBenhVien.Persistence.Seed;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence.Seed;

public class RoleDefaultsPlanTests
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Matrix = new()
    {
        ["doctor"] = ["patients.read", "encounters.read"],
        ["receptionist"] = ["patients.read"],
    };

    [Fact]
    public void ReturnsOnlyActivatedAndNotYetAppliedPairs()
    {
        var applied = new HashSet<(string, string)> { ("receptionist", "patients.read") };
        var pending = RoleDefaultsPlan.PendingPairs(Matrix, c => c == "patients.read", applied);
        Assert.Equal([("doctor", "patients.read")], pending);
    }

    [Fact]
    public void NotActivatedCode_IsSkipped()
        => Assert.Empty(RoleDefaultsPlan.PendingPairs(Matrix, _ => false, new HashSet<(string, string)>()));
}

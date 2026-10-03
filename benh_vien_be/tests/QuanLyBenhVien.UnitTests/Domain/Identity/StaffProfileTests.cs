using QuanLyBenhVien.Domain.Identity.Staff;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Domain.Identity;

public class StaffProfileTests
{
    [Fact]
    public void Create_Valid()
    {
        var userId = Guid.NewGuid();
        var p = StaffProfile.Create(userId, "BS-001");
        Assert.Equal(userId, p.UserId);
        Assert.Equal("BS-001", p.StaffCode);
        Assert.True(p.IsActive);
        Assert.Empty(p.WorkScopes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("bs-001")]
    [InlineData("-AB")]
    [InlineData("A")]
    [InlineData("AB\n")]
    public void Create_InvalidCode_Throws(string code)
        => Assert.Throws<ArgumentException>(() => StaffProfile.Create(Guid.NewGuid(), code));

    [Fact]
    public void ChangeStaffCode_Invalid_Throws()
        => Assert.Throws<ArgumentException>(() => StaffProfile.Create(Guid.NewGuid(), "AB").ChangeStaffCode("x y"));

    [Fact]
    public void SetWorkScopes_ReplacesSet_DedupsAndReportsChange()
    {
        var p = StaffProfile.Create(Guid.NewGuid(), "AB");
        var b = Guid.NewGuid();
        var d1 = Guid.NewGuid();
        var d2 = Guid.NewGuid();
        Assert.True(p.SetWorkScopes([(d1, b), (d1, b), (d2, b)]));
        Assert.Equal(2, p.WorkScopes.Count);
        Assert.False(p.SetWorkScopes([(d2, b), (d1, b)]));
        Assert.True(p.SetWorkScopes([(d1, b)]));
        Assert.Single(p.WorkScopes);
        Assert.True(p.SetWorkScopes([]));
        Assert.Empty(p.WorkScopes);
    }
}

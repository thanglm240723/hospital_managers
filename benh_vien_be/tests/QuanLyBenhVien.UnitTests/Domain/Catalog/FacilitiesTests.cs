using Xunit;
using QuanLyBenhVien.Domain.Catalog.Facilities;

namespace QuanLyBenhVien.UnitTests.Domain.Catalog;

public sealed class FacilitiesTests
{
    [Theory]
    [InlineData("ab")]
    [InlineData("A B")]
    [InlineData("ABCDEFGHIJABCDEFGHIJABCDEFGHIJX")]
    [InlineData("-A")]
    [InlineData("CS1\n")]
    [InlineData("")]
    public void Branch_InvalidCode_Throws(string code)
    {
        Assert.Throws<ArgumentException>(() => Branch.Create(code, "Cơ sở 1"));
        Assert.Throws<ArgumentException>(() => Department.Create(Guid.CreateVersion7(), code, "Khoa", DepartmentKind.Clinical));
        Assert.Throws<ArgumentException>(() => Room.Create(Guid.CreateVersion7(), code, "Phòng"));
    }

    [Fact]
    public void Branch_Create_Valid()
    {
        var b = Branch.Create("CS-1", "  Cơ sở 1 ");
        Assert.NotEqual(Guid.Empty, b.Id);
        Assert.Equal("Cơ sở 1", b.Name);
        Assert.True(b.IsActive);
    }

    [Fact]
    public void Department_Create_TrimsName_AndKeepsKind()
    {
        var d = Department.Create(Guid.CreateVersion7(), "NOI", "  Khoa Nội  ", DepartmentKind.Clinical);
        Assert.Equal("Khoa Nội", d.Name);
        Assert.Equal(DepartmentKind.Clinical, d.Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankName_Throws(string name)
    {
        Assert.Throws<ArgumentException>(() => Branch.Create("A", name));
        Assert.Throws<ArgumentException>(() => Department.Create(Guid.CreateVersion7(), "A", name, DepartmentKind.Clinical));
        Assert.Throws<ArgumentException>(() => Room.Create(Guid.CreateVersion7(), "A", name));
    }

    [Fact]
    public void Name_TooLong_Throws() => Assert.Throws<ArgumentException>(() => Room.Create(Guid.CreateVersion7(), "A", new string('x', 201)));

    [Fact]
    public void Rename_And_SetActive_Change_Values()
    {
        var r = Room.Create(Guid.CreateVersion7(), "P1", "Phòng 1");
        r.Rename(" Phòng 2 ");
        r.SetActive(false);
        Assert.Equal("Phòng 2", r.Name);
        Assert.False(r.IsActive);
        Assert.Throws<ArgumentException>(() => r.Rename(" "));
    }

    [Fact]
    public void Code_And_Kind_HaveNoPublicSetter()
    {
        Assert.False(typeof(Branch).GetProperty(nameof(Branch.Code))!.SetMethod!.IsPublic);
        Assert.False(typeof(Department).GetProperty(nameof(Department.Code))!.SetMethod!.IsPublic);
        Assert.False(typeof(Department).GetProperty(nameof(Department.Kind))!.SetMethod!.IsPublic);
        Assert.False(typeof(Room).GetProperty(nameof(Room.Code))!.SetMethod!.IsPublic);
    }
}

using QuanLyBenhVien.Application.Features.Facilities.CreateBranch;
using QuanLyBenhVien.Application.Features.Facilities.CreateDepartment;
using QuanLyBenhVien.Application.Features.Facilities.CreateRoom;
using QuanLyBenhVien.Application.Features.Facilities.UpdateRoom;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Facilities;

public class FacilityValidatorsTests
{
    private static readonly Guid Id = Guid.NewGuid();

    [Theory]
    [InlineData("clinical")]
    [InlineData("laboratory")]
    [InlineData("pharmacy")]
    [InlineData("billing")]
    [InlineData("administrative")]
    public void CreateDepartment_ValidKind_Passes(string kind)
        => Assert.True(new CreateDepartmentCommandValidator().Validate(new CreateDepartmentCommand(Id, "KHOA-1", "Khoa nội", kind)).IsValid);

    [Theory]
    [InlineData("unknown")]
    [InlineData("Clinical")]
    [InlineData("1")]
    [InlineData("")]
    public void CreateDepartment_BadKind_Fails(string kind)
        => Assert.False(new CreateDepartmentCommandValidator().Validate(new CreateDepartmentCommand(Id, "KHOA-1", "Khoa nội", kind)).IsValid);

    [Theory]
    [InlineData("khoa-1")]
    [InlineData("-A")]
    [InlineData("A B")]
    [InlineData("A\n")]
    [InlineData("")]
    public void CreateDepartment_BadCode_Fails(string code)
        => Assert.False(new CreateDepartmentCommandValidator().Validate(new CreateDepartmentCommand(Id, code, "Khoa nội", "clinical")).IsValid);

    [Fact]
    public void CreateBranch_LongCodeOrName_Fails()
    {
        var validator = new CreateBranchCommandValidator();
        Assert.False(validator.Validate(new CreateBranchCommand(new string('A', 31), "CS1")).IsValid);
        Assert.False(validator.Validate(new CreateBranchCommand("CS1", new string('x', 201))).IsValid);
        Assert.True(validator.Validate(new CreateBranchCommand("CS1", "Cơ sở 1")).IsValid);
    }

    [Fact]
    public void CreateRoom_EmptyDepartmentOrName_Fails()
    {
        var validator = new CreateRoomCommandValidator();
        Assert.False(validator.Validate(new CreateRoomCommand(Guid.Empty, "P1", "Phòng 1")).IsValid);
        Assert.False(validator.Validate(new CreateRoomCommand(Id, "P1", " ")).IsValid);
    }

    [Fact]
    public void UpdateRoom_EmptyName_Fails()
        => Assert.False(new UpdateRoomCommandValidator().Validate(new UpdateRoomCommand(Id, "", true, 1)).IsValid);
}

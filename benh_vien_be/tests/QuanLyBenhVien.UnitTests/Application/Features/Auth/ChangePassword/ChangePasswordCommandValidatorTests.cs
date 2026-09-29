using QuanLyBenhVien.Application.Features.Auth.ChangePassword;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Auth.ChangePassword;

public class ChangePasswordCommandValidatorTests
{
    private readonly ChangePasswordCommandValidator _validator = new();

    [Fact]
    public void CurrentPassword_Empty_HasValidationError()
    {
        var result = _validator.Validate(new ChangePasswordCommand("", "Valid-New-Pass1"));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword));
    }

    [Fact]
    public void CurrentPassword_Null_HasValidationError()
    {
        var result = _validator.Validate(new ChangePasswordCommand(null!, "Valid-New-Pass1"));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.CurrentPassword));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void NewPassword_NullOrEmpty_HasValidationError(string? newPassword)
    {
        var result = _validator.Validate(new ChangePasswordCommand("Current-Pass1", newPassword!));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void NewPassword_9Characters_HasValidationError()
    {
        var result = _validator.Validate(new ChangePasswordCommand("Current-Pass1", new string('a', 9)));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void NewPassword_10Characters_IsValid()
    {
        var result = _validator.Validate(new ChangePasswordCommand("Current-Pass1", new string('a', 10)));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void NewPassword_128Characters_IsValid()
    {
        var result = _validator.Validate(new ChangePasswordCommand("Current-Pass1", new string('a', 128)));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void NewPassword_129Characters_HasValidationError()
    {
        var result = _validator.Validate(new ChangePasswordCommand("Current-Pass1", new string('a', 129)));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }

    [Fact]
    public void NewPassword_WithLeadingAndTrailingWhitespace_IsNotTrimmed()
    {
        // Khoảng trắng được giữ nguyên: 8 ký tự thật + 2 khoảng trắng đầu/cuối = 10 -> hợp lệ vì không trim.
        var newPassword = "  abcdefgh  ";
        Assert.Equal(12, newPassword.Length);

        var result = _validator.Validate(new ChangePasswordCommand("Current-Pass1", newPassword));

        Assert.DoesNotContain(result.Errors, e => e.PropertyName == nameof(ChangePasswordCommand.NewPassword));
    }
}

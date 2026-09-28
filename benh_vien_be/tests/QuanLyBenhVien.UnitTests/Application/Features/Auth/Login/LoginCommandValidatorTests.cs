using QuanLyBenhVien.Application.Features.Auth.Login;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Auth.Login;

public class LoginCommandValidatorTests
{
    private readonly LoginCommandValidator _validator = new();

    [Fact]
    public void EmptyFields_HasErrorsForBothEmailAndPassword()
    {
        var result = _validator.Validate(new LoginCommand("", ""));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Email));
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Password));
    }

    [Fact]
    public void InvalidEmailFormat_HasError()
    {
        var result = _validator.Validate(new LoginCommand("not-an-email", "Some-Password-1"));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Email));
    }

    [Fact]
    public void EmailTooLong_HasError()
    {
        var longEmail = new string('a', 250) + "@a.vn";
        var result = _validator.Validate(new LoginCommand(longEmail, "Some-Password-1"));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Email));
    }

    [Fact]
    public void PasswordTooLong_HasError()
    {
        var result = _validator.Validate(new LoginCommand("a@b.vn", new string('a', 129)));

        Assert.Contains(result.Errors, e => e.PropertyName == nameof(LoginCommand.Password));
    }

    [Fact]
    public void ValidInput_HasNoErrors()
    {
        var result = _validator.Validate(new LoginCommand("a@b.vn", "Some-Password-1"));

        Assert.True(result.IsValid);
    }
}

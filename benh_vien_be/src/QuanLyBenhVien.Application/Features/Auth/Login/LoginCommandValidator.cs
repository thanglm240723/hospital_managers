using FluentValidation;

namespace QuanLyBenhVien.Application.Features.Auth.Login;

internal sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email không được để trống.")
            .EmailAddress().WithMessage("Email không đúng định dạng.")
            .MaximumLength(256).WithMessage("Email không được dài quá 256 ký tự.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .MaximumLength(128).WithMessage("Mật khẩu không được dài quá 128 ký tự.");
    }
}

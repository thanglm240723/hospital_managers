using CleanArchCqrs.Application.Auth.Commands.Login;
using FluentValidation;

namespace CleanArchCqrs.Application.Auth.Validators;

/// Không kiểm độ mạnh mật khẩu ở login — quy tắc đó thuộc về đặt/đổi mật khẩu.
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}

using CleanArchCqrs.Application.Auth.Commands.ChangePassword;
using CleanArchCqrs.Application.Common.Security;
using FluentValidation;

namespace CleanArchCqrs.Application.Auth.Validators;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NewPassword();
    }
}

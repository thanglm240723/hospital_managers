using CleanArchCqrs.Application.Common.Security;
using CleanArchCqrs.Application.Users.Commands.CreateUser;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TemporaryPassword).NewPassword()
            .Must((command, password) => !PasswordPolicy.ContainsEmailLocalPart(password, command.Email))
            .WithMessage("Mật khẩu không được chứa phần tên trong email.");
        RuleFor(x => x.RoleIds).NotNull();
    }
}

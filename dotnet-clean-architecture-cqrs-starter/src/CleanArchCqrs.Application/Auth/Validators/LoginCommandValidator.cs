using CleanArchCqrs.Application.Auth.Commands.Login;
using FluentValidation;


namespace CleanArchCqrs.Application.Auth.Validators
{
    public class LoginCommandValidator : AbstractValidator<LoginCommand>
    {
        public LoginCommandValidator()
        {
            RuleFor(x => x.email).NotEmpty().EmailAddress();
            RuleFor(x => x.password).NotEmpty();
        }
    }
}

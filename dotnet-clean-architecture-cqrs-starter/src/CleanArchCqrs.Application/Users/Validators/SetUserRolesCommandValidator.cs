using CleanArchCqrs.Application.Users.Commands.SetUserRoles;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class SetUserRolesCommandValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesCommandValidator() => RuleFor(x => x.RoleIds).NotNull();
}

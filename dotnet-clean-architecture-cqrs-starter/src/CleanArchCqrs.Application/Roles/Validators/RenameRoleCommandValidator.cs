using CleanArchCqrs.Application.Roles.Commands.RenameRole;
using FluentValidation;

namespace CleanArchCqrs.Application.Roles.Validators;

public sealed class RenameRoleCommandValidator : AbstractValidator<RenameRoleCommand>
{
    public RenameRoleCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}

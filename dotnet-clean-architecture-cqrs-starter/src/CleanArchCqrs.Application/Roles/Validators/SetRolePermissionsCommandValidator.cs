using CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;
using CleanArchCqrs.Domain.Identity;
using FluentValidation;

namespace CleanArchCqrs.Application.Roles.Validators;

public sealed class SetRolePermissionsCommandValidator : AbstractValidator<SetRolePermissionsCommand>
{
    public SetRolePermissionsCommandValidator()
    {
        RuleFor(x => x.PermissionCodes).NotNull();
        RuleForEach(x => x.PermissionCodes).Must(Permissions.IsDefined).WithMessage("Quyền không có trong danh mục.");
    }
}

using CleanArchCqrs.Application.Users.Commands.RevokeUserPermission;
using CleanArchCqrs.Domain.Identity;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class RevokeUserPermissionCommandValidator : AbstractValidator<RevokeUserPermissionCommand>
{
    public RevokeUserPermissionCommandValidator()
    {
        RuleFor(x => x.PermissionCode).NotEmpty().Must(Permissions.IsDefined).WithMessage("Quyền không có trong danh mục.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}

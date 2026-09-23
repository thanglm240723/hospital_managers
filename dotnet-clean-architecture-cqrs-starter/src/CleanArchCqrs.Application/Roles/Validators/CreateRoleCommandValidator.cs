using CleanArchCqrs.Application.Roles.Commands.CreateRole;
using CleanArchCqrs.Domain.Identity;
using FluentValidation;

namespace CleanArchCqrs.Application.Roles.Validators;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Code).Must(Role.IsValidCode).WithMessage("Mã vai trò chỉ gồm chữ thường, số, gạch nối; 2–50 ký tự.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PermissionCodes).NotNull();
        RuleForEach(x => x.PermissionCodes).Must(Permissions.IsDefined).WithMessage("Quyền không có trong danh mục.");
    }
}

using FluentValidation;
using QuanLyBenhVien.Application.Features.Roles.Common;
using QuanLyBenhVien.Domain.Identity;

namespace QuanLyBenhVien.Application.Features.Roles.CreateRole;

internal sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Code)
            .Must(c => c is not null && Role.IsValidCode(c))
            .WithMessage("Mã vai trò phải là chữ thường kebab-case, dài 2–50 ký tự.");
        RuleFor(x => x.Name).RolesName();
        RuleFor(x => x.PermissionCodes).RolesPermissionCodes();
    }
}

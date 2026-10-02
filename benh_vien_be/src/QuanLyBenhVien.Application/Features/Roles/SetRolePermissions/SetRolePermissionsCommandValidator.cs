using FluentValidation;
using QuanLyBenhVien.Application.Features.Roles.Common;

namespace QuanLyBenhVien.Application.Features.Roles.SetRolePermissions;

internal sealed class SetRolePermissionsCommandValidator : AbstractValidator<SetRolePermissionsCommand>
{
    public SetRolePermissionsCommandValidator() => RuleFor(x => x.PermissionCodes).RolesPermissionCodes();
}

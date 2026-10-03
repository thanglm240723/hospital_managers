using FluentValidation;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.GrantUserPermission;

internal sealed class GrantUserPermissionCommandValidator : AbstractValidator<GrantUserPermissionCommand>
{
    public GrantUserPermissionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.PermissionCode).UsersPermissionCode();
        RuleFor(x => x.Reason).UsersReason();
    }
}

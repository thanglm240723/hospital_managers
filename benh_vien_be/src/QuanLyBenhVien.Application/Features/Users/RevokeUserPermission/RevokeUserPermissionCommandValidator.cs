using FluentValidation;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.RevokeUserPermission;

internal sealed class RevokeUserPermissionCommandValidator : AbstractValidator<RevokeUserPermissionCommand>
{
    public RevokeUserPermissionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.PermissionCode).UsersPermissionCode();
        RuleFor(x => x.Reason).UsersReason();
    }
}

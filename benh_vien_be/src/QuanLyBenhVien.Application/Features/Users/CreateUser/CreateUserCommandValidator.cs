using FluentValidation;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.CreateUser;

internal sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email)
            .Must(e => !string.IsNullOrWhiteSpace(e)).WithMessage("Vui lòng nhập email.")
            .Must(e => e is null || e.Trim().Length <= 256).WithMessage("Email tối đa 256 ký tự.")
            .Must(e => e is null || IsEmail(e.Trim())).WithMessage("Email không hợp lệ.");
        RuleFor(x => x.FullName)
            .Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("Vui lòng nhập họ tên.")
            .Must(n => n is null || n.Trim().Length <= 200).WithMessage("Họ tên tối đa 200 ký tự.");
        RuleFor(x => x.RoleIds).UsersRoleIds();
    }

    private static bool IsEmail(string value)
    {
        var at = value.IndexOf('@');
        return at > 0 && at == value.LastIndexOf('@') && at < value.Length - 1 && !value.Any(char.IsWhiteSpace);
    }
}

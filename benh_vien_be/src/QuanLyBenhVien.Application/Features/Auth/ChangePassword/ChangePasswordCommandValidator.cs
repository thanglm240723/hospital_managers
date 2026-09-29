using FluentValidation;
using QuanLyBenhVien.Application.Common.Security;

namespace QuanLyBenhVien.Application.Features.Auth.ChangePassword;

internal sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty().WithMessage("Vui lòng nhập mật khẩu hiện tại.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Vui lòng nhập mật khẩu mới.")
            .Length(PasswordPolicy.MinLength, PasswordPolicy.MaxLength)
            .WithMessage($"Mật khẩu mới phải có từ {PasswordPolicy.MinLength} đến {PasswordPolicy.MaxLength} ký tự.");
    }
}

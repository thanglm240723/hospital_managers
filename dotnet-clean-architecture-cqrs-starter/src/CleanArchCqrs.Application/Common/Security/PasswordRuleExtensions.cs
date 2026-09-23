using FluentValidation;

namespace CleanArchCqrs.Application.Common.Security;

public static class PasswordRuleExtensions
{
    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .Length(PasswordPolicy.MinLength, PasswordPolicy.MaxLength)
            .WithMessage($"Mật khẩu phải dài từ {PasswordPolicy.MinLength} đến {PasswordPolicy.MaxLength} ký tự.");
}

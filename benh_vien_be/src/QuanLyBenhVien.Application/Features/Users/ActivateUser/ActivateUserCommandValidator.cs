using FluentValidation;

namespace QuanLyBenhVien.Application.Features.Users.ActivateUser;

internal sealed class ActivateUserCommandValidator : AbstractValidator<ActivateUserCommand>
{
    public ActivateUserCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

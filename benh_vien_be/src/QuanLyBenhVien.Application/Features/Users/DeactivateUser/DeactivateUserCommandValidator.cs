using FluentValidation;

namespace QuanLyBenhVien.Application.Features.Users.DeactivateUser;

internal sealed class DeactivateUserCommandValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserCommandValidator() => RuleFor(x => x.Id).NotEmpty();
}

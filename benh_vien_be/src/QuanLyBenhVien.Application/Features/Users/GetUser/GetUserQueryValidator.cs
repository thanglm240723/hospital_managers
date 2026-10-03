using FluentValidation;

namespace QuanLyBenhVien.Application.Features.Users.GetUser;

internal sealed class GetUserQueryValidator : AbstractValidator<GetUserQuery>
{
    public GetUserQueryValidator()
        => RuleFor(x => x.Id).NotEmpty().WithMessage("Mã tài khoản không hợp lệ.");
}

using FluentValidation;
using QuanLyBenhVien.Application.Features.Users.Common;

namespace QuanLyBenhVien.Application.Features.Users.ListUsers;

internal sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1).WithMessage("pageNumber phải từ 1 trở lên.");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).WithMessage("pageSize phải từ 1 đến 100.");
        RuleFor(x => x.SearchTerm).MaximumLength(200).WithMessage("searchTerm tối đa 200 ký tự.");
        RuleFor(x => x.Status)
            .Must(UserStatuses.IsValid).When(x => x.Status is not null)
            .WithMessage("status phải là active, must_change_password hoặc locked.");
    }
}

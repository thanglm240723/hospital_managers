using CleanArchCqrs.Application.Users.Queries.GetUsers;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SearchTerm).MaximumLength(100);
    }
}

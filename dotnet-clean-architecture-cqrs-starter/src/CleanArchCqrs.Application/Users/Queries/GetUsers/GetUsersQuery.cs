using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Models;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUsers;

public sealed record GetUsersQuery(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null)
    : IRequest<PagedResult<UserSummaryDto>>;

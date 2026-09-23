using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Models;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUsers;

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PagedResult<UserSummaryDto>>
{
    private readonly IIdentityReadService _read;

    public GetUsersQueryHandler(IIdentityReadService read) => _read = read;

    public Task<PagedResult<UserSummaryDto>> Handle(GetUsersQuery request, CancellationToken ct)
        => _read.GetUsersAsync(request.PageNumber, request.PageSize, request.SearchTerm, ct);
}

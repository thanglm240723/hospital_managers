using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Roles.Models;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Queries.GetRoles;

public sealed class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IIdentityReadService _read;

    public GetRolesQueryHandler(IIdentityReadService read) => _read = read;

    public Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct) => _read.GetRolesAsync(ct);
}

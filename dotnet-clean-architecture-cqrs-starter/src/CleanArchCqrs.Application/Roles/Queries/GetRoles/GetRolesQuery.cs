using CleanArchCqrs.Application.Roles.Models;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Queries.GetRoles;

public sealed record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

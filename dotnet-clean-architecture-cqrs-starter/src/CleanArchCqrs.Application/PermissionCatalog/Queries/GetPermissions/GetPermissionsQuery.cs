using CleanArchCqrs.Application.PermissionCatalog.Models;
using MediatR;

namespace CleanArchCqrs.Application.PermissionCatalog.Queries.GetPermissions;

public sealed record GetPermissionsQuery : IRequest<IReadOnlyList<PermissionDto>>;

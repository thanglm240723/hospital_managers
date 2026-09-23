using CleanArchCqrs.Application.PermissionCatalog.Models;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.PermissionCatalog.Queries.GetPermissions;

/// Danh mục là code (seeder đồng bộ xuống DB) — đọc thẳng từ code, không cần DB.
public sealed class GetPermissionsQueryHandler : IRequestHandler<GetPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    public Task<IReadOnlyList<PermissionDto>> Handle(GetPermissionsQuery request, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PermissionDto>>(Permissions.All
            .OrderBy(p => p.Group).ThenBy(p => p.Code, StringComparer.Ordinal)
            .Select(p => new PermissionDto(p.Code, p.Group, p.Description))
            .ToList());
}

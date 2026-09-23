using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.Application.PermissionCatalog.Models;
using CleanArchCqrs.Application.PermissionCatalog.Queries.GetPermissions;
using CleanArchCqrs.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/permissions")]
public sealed class PermissionsController : ControllerBase
{
    private readonly ISender _mediator;

    public PermissionsController(ISender mediator) => _mediator = mediator;

    [HasPermission(Permissions.Catalog.Read)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetAll(CancellationToken ct)
        => Ok(await _mediator.Send(new GetPermissionsQuery(), ct));
}

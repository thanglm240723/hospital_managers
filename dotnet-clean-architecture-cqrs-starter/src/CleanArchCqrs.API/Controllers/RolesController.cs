using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.API.Contracts.Roles;
using CleanArchCqrs.Application.Roles.Commands.CreateRole;
using CleanArchCqrs.Application.Roles.Commands.RenameRole;
using CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;
using CleanArchCqrs.Application.Roles.Models;
using CleanArchCqrs.Application.Roles.Queries.GetRoles;
using CleanArchCqrs.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/roles")]
public sealed class RolesController : ControllerBase
{
    private readonly ISender _mediator;

    public RolesController(ISender mediator) => _mediator = mediator;

    [HasPermission(Permissions.Roles.Read)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct)
        => Ok(await _mediator.Send(new GetRolesQuery(), ct));

    [HasPermission(Permissions.Roles.Manage)]
    [CsrfProtected]
    [HttpPost]
    public async Task<IActionResult> Create(CreateRoleCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Created($"/api/v1/roles/{id}", new { id });
    }

    [HasPermission(Permissions.Roles.Manage)]
    [CsrfProtected]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, RenameRoleRequest request, CancellationToken ct)
    {
        await _mediator.Send(new RenameRoleCommand(id, request.Name), ct);
        return NoContent();
    }

    [HasPermission(Permissions.Roles.Manage)]
    [CsrfProtected]
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid id, SetRolePermissionsRequest request, CancellationToken ct)
    {
        await _mediator.Send(new SetRolePermissionsCommand(id, request.PermissionCodes), ct);
        return NoContent();
    }
}

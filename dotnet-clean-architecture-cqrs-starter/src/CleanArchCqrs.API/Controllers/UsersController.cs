using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Commands.ActivateUser;
using CleanArchCqrs.Application.Users.Commands.CreateUser;
using CleanArchCqrs.Application.Users.Commands.DeactivateUser;
using CleanArchCqrs.Application.Users.Models;
using CleanArchCqrs.Application.Users.Queries.GetUser;
using CleanArchCqrs.Application.Users.Queries.GetUsers;
using CleanArchCqrs.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    private readonly ISender _mediator;

    public UsersController(ISender mediator) => _mediator = mediator;

    [HasPermission(Permissions.Users.Read)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> List([FromQuery] GetUsersQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(query, ct));

    [HasPermission(Permissions.Users.Read)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetUserQuery(id), ct));

    [HasPermission(Permissions.Users.Create)]
    [CsrfProtected]
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HasPermission(Permissions.Users.Activate)]
    [CsrfProtected]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateUserCommand(id), ct);
        return NoContent();
    }

    [HasPermission(Permissions.Users.Activate)]
    [CsrfProtected]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ActivateUserCommand(id), ct);
        return NoContent();
    }
}

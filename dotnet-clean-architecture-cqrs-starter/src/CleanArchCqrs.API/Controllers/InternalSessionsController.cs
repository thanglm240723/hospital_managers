using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Contracts.Internal;
using CleanArchCqrs.Application.Auth.Queries.ValidateSession;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("internal/sessions")]
[AllowAnonymous]
[InternalApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalSessionsController : ControllerBase
{
    private readonly ISender _mediator;

    public InternalSessionsController(ISender mediator) => _mediator = mediator;

    [HttpPost("validate")]
    public async Task<ActionResult<ValidateSessionResponse>> Validate(ValidateSessionRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ValidateSessionQuery(request.FamilyId, request.Sv), ct);
        return Ok(new ValidateSessionResponse(result.IsValid, result.UserId, result.AbsoluteExpiresAtUtc?.ToUnixTimeSeconds()));
    }
}

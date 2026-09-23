using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Contracts.Auth;
using CleanArchCqrs.Application.Auth.Commands.Login;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly AuthCookieWriter _cookies;

    public AuthController(ISender mediator, AuthCookieWriter cookies)
    {
        _mediator = mediator;
        _cookies = cookies;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AccessTokenResponse>> Login(LoginCommand command, CancellationToken ct)
    {
        var session = await _mediator.Send(command, ct);
        _cookies.Write(Response, session.RefreshToken, session.SessionFamilyId, session.SessionExpiresAtUtc);
        return Ok(new AccessTokenResponse(session.AccessToken, session.AccessTokenExpiresAtUtc, session.MustChangePassword));
    }
}

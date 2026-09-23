using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.API.Contracts.Auth;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Auth;
using CleanArchCqrs.Application.Auth.Commands.ChangePassword;
using CleanArchCqrs.Application.Auth.Commands.Login;
using CleanArchCqrs.Application.Auth.Commands.Logout;
using CleanArchCqrs.Application.Auth.Commands.LogoutAll;
using CleanArchCqrs.Application.Auth.Commands.Refresh;
using CleanArchCqrs.Application.Auth.Commands.RevokeSession;
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Auth.Queries.GetMe;
using CleanArchCqrs.Application.Auth.Queries.GetMySessions;
using CleanArchCqrs.Application.Common.Exceptions;
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

    [AllowAnonymous]
    [CsrfProtected]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (string.IsNullOrEmpty(refreshToken)) return SessionInvalid(AuthMessages.SessionInvalid);

        try
        {
            var session = await _mediator.Send(new RefreshCommand(refreshToken), ct);
            _cookies.Write(Response, session.RefreshToken, session.SessionFamilyId, session.SessionExpiresAtUtc);
            return Ok(new AccessTokenResponse(session.AccessToken, session.AccessTokenExpiresAtUtc, session.MustChangePassword));
        }
        catch (UnauthorizedException ex)
        {
            // Trả lỗi tại đây (không để exception handler) vì handler xoá sạch header, gồm cả Set-Cookie xoá cookie.
            return SessionInvalid(ex.Message);
        }
    }

    [AllowAnonymous]
    [CsrfProtected]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _mediator.Send(new LogoutCommand(Request.Cookies[AuthCookieWriter.RefreshCookie]), ct);
        _cookies.Clear(Response);
        return NoContent();
    }

    [AllowWhilePasswordChangeRequired]
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMeQuery(), ct));

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMySessionsQuery(), ct));

    [CsrfProtected]
    [HttpPost("sessions/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RevokeSessionCommand(id), ct);
        return NoContent();
    }

    [AllowWhilePasswordChangeRequired]
    [CsrfProtected]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await _mediator.Send(new LogoutAllCommand(), ct);
        _cookies.Clear(Response);
        return NoContent();
    }

    [AllowWhilePasswordChangeRequired]
    [CsrfProtected]
    [HttpPost("change-password")]
    public async Task<ActionResult<AccessTokenResponse>> ChangePassword(ChangePasswordCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(new AccessTokenResponse(result.AccessToken, result.ExpiresAtUtc, result.MustChangePassword));
    }

    private IActionResult SessionInvalid(string message)
    {
        _cookies.Clear(Response);
        return ProblemResponseWriter.ToResult(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, message);
    }
}

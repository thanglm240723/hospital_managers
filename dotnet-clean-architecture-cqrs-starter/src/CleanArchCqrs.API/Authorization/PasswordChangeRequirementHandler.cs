using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

public sealed class PasswordChangeRequirementHandler : AuthorizationHandler<PasswordChangeRequirement>
{
    private readonly IPermissionService _permissions;

    public PasswordChangeRequirementHandler(IPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PasswordChangeRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)) return;   // chưa đăng nhập: để RequireAuthenticatedUser xử lý

        if (context.Resource is HttpContext http
            && http.GetEndpoint()?.Metadata.GetMetadata<AllowWhilePasswordChangeRequiredAttribute>() is not null)
        {
            context.Succeed(requirement);
            return;
        }

        if ((await _permissions.GetAsync(userId)).MustChangePassword)
            context.Fail(new AuthorizationFailureReason(this, ErrorCodes.PasswordChangeRequired));
        else
            context.Succeed(requirement);
    }
}

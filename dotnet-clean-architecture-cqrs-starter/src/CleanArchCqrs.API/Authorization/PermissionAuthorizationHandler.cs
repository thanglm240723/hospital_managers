using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;

    public PermissionAuthorizationHandler(IPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)) return;
        var access = await _permissions.GetAsync(userId);
        if (access.Permissions.Contains(requirement.Permission)) context.Succeed(requirement);
    }
}

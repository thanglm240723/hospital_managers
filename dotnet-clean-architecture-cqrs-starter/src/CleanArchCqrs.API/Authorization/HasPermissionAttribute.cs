using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

/// Dùng hằng trong Domain.Identity.Permissions, ví dụ [HasPermission(Permissions.Users.Read)].
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) => Policy = PermissionPolicyProvider.PolicyPrefix + permission;
}

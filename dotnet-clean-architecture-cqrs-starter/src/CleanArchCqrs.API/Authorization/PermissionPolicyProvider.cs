using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Authorization;

/// Sinh policy "perm:<mã>" động — không phải khai báo từng policy trong Program.cs.
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "perm:";

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
    {
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangeRequirement(), new PermissionRequirement(policyName[PolicyPrefix.Length..]))
            .Build();
    }
}

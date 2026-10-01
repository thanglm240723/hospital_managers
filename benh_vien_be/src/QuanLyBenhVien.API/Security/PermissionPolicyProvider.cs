using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using QuanLyBenhVien.Presentation.Auth;

namespace QuanLyBenhVien.API.Security;

/// Dựng policy `perm:<code>` theo yêu cầu; tên khác (và fallback/default) giữ hành vi mặc định.
public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (policyName.StartsWith(PermissionPolicy.Prefix, StringComparison.Ordinal))
        {
            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PermissionRequirement(policyName[PermissionPolicy.Prefix.Length..]))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

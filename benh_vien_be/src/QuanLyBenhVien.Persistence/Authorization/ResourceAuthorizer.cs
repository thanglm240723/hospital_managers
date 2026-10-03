using Microsoft.Extensions.Logging;
using QuanLyBenhVien.Application.Common.Authorization;

namespace QuanLyBenhVien.Persistence.Authorization;

internal sealed class ResourceAuthorizer : IResourceAuthorizer
{
    private readonly Dictionary<(string, string), IResourceScopePolicy> _policies;
    private readonly IAccessContext _accessContext;
    private readonly TimeProvider _time;
    private readonly ILogger<ResourceAuthorizer> _logger;

    public ResourceAuthorizer(IEnumerable<IResourceScopePolicy> policies, IAccessContext accessContext, TimeProvider time,
        ILogger<ResourceAuthorizer> logger)
    {
        _accessContext = accessContext;
        _time = time;
        _logger = logger;
        _policies = new();
        foreach (var p in policies)
        {
            if (!_policies.TryAdd((p.PermissionCode, p.ResourceType), p))
                throw new InvalidOperationException($"Duplicate resource policy for {p.PermissionCode} {p.ResourceType}.");
        }
    }

    public async Task<AccessDecision> AuthorizeAsync(string permissionCode, ResourceRef resource, CancellationToken ct)
    {
        if (!_policies.TryGetValue((permissionCode, resource.ResourceType), out var policy))
        {
            _logger.LogError("No resource policy for {Permission} {ResourceType}", permissionCode, resource.ResourceType);
            return new AccessDecision.Denied("no_policy");
        }

        var scope = await _accessContext.GetAsync(ct);
        if (!scope.HasWorkScope)
            return new AccessDecision.Denied("no_work_scope");

        return await policy.EvaluateAsync(scope, resource.Id, _time.GetUtcNow(), ct);
    }
}

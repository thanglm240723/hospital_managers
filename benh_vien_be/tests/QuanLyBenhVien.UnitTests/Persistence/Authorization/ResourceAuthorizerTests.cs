using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QuanLyBenhVien.Application.Common.Authorization;
using QuanLyBenhVien.Persistence.Authorization;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence.Authorization;

public class ResourceAuthorizerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 1, 0, 0, TimeSpan.Zero);
    private static readonly Guid Dept = Guid.NewGuid();

    private sealed class FakeContext(AccessScope scope) : IAccessContext
    {
        public Task<AccessScope> GetAsync(CancellationToken ct) => Task.FromResult(scope);
    }

    private sealed class FakePolicy(string perm, string type, AccessDecision decision) : IResourceScopePolicy
    {
        public int Calls;
        public DateTimeOffset? SeenNow;
        public string PermissionCode => perm;
        public string ResourceType => type;
        public Task<AccessDecision> EvaluateAsync(AccessScope scope, Guid resourceId, DateTimeOffset now, CancellationToken ct)
        {
            Calls++;
            SeenNow = now;
            return Task.FromResult(decision);
        }
    }

    private static AccessScope WithScope() => new(Guid.NewGuid(), Guid.NewGuid(), new HashSet<Guid> { Guid.NewGuid() }, new HashSet<Guid> { Dept });
    private static AccessScope NoScope() => new(Guid.NewGuid(), null, new HashSet<Guid>(), new HashSet<Guid>());

    private static ResourceAuthorizer Create(AccessScope scope, params IResourceScopePolicy[] policies)
        => new(policies, new FakeContext(scope), new FakeTimeProvider(Now), NullLogger<ResourceAuthorizer>.Instance);

    [Fact]
    public async Task NoPolicy_Denied()
    {
        var result = await Create(WithScope()).AuthorizeAsync("p", new ResourceRef("X", Guid.NewGuid()), default);
        Assert.Equal("no_policy", Assert.IsType<AccessDecision.Denied>(result).Reason);
    }

    [Fact]
    public async Task NoWorkScope_Denied_PolicyNotCalled()
    {
        var policy = new FakePolicy("p", "X", new AccessDecision.Allowed(RelationKind.Grant, null));
        var result = await Create(NoScope(), policy).AuthorizeAsync("p", new ResourceRef("X", Guid.NewGuid()), default);
        Assert.Equal("no_work_scope", Assert.IsType<AccessDecision.Denied>(result).Reason);
        Assert.Equal(0, policy.Calls);
    }

    [Fact]
    public async Task WithScope_ReturnsPolicyDecision_AndPassesNow()
    {
        var expected = new AccessDecision.Allowed(RelationKind.DepartmentScope, "doctor");
        var policy = new FakePolicy("p", "X", expected);
        var result = await Create(WithScope(), policy).AuthorizeAsync("p", new ResourceRef("X", Guid.NewGuid()), default);
        Assert.Same(expected, result);
        Assert.Equal(Now, policy.SeenNow);
    }

    [Fact]
    public void DuplicatePair_Throws()
    {
        var a = new FakePolicy("p", "X", new AccessDecision.Denied("a"));
        var b = new FakePolicy("p", "X", new AccessDecision.Denied("b"));
        Assert.Throws<InvalidOperationException>(() => Create(WithScope(), a, b));
    }
}

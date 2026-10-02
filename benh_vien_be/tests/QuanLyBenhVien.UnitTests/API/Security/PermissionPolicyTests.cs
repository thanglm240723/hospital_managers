using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuanLyBenhVien.API.Security;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Presentation.Security;
using Xunit;

namespace QuanLyBenhVien.UnitTests.API.Security;

public class PermissionPolicyTests
{
    private sealed class FakePermissionService(UserAccessDto? access) : IPermissionService
    {
        public Task<UserAccessDto?> GetAsync(Guid userId, CancellationToken ct) => Task.FromResult(access);
    }

    private static UserAccessDto Access(bool mustChange = false, bool active = true, params string[] permissions)
        => new(active, mustChange, permissions.ToHashSet());

    private static AuthorizationHandlerContext Context(UserAccessDto? access, string permission, bool allowWhileMustChange = false, bool authenticated = true)
    {
        var services = new ServiceCollection().AddSingleton<IPermissionService>(new FakePermissionService(access)).BuildServiceProvider();
        var metadata = allowWhileMustChange
            ? new EndpointMetadataCollection(AllowWhilePasswordChangeRequiredMetadata.Instance)
            : EndpointMetadataCollection.Empty;
        var http = new DefaultHttpContext { RequestServices = services };
        http.SetEndpoint(new Endpoint(null, metadata, "probe"));
        var user = authenticated
            ? new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", Guid.NewGuid().ToString())], "Bearer"))
            : new ClaimsPrincipal(new ClaimsIdentity());
        return new AuthorizationHandlerContext([new PermissionRequirement(permission)], user, http);
    }

    private static Task Handle(AuthorizationHandlerContext context) => new PermissionAuthorizationHandler().HandleAsync(context);

    [Fact]
    public async Task Provider_BuildsPolicyForPermPrefix()
    {
        var provider = new PermissionPolicyProvider(Options.Create(new AuthorizationOptions()));

        var policy = await provider.GetPolicyAsync(PermissionPolicy.NameFor(Permissions.Users.Read));

        Assert.NotNull(policy);
        Assert.Equal("perm:users.read", PermissionPolicy.NameFor(Permissions.Users.Read));
        Assert.Contains(policy!.Requirements, r => r is PermissionRequirement { Permission: Permissions.Users.Read });
        Assert.Contains(policy.Requirements, r => r is DenyAnonymousAuthorizationRequirement);
    }

    [Fact]
    public async Task Provider_UnknownPolicy_ReturnsNull_AndFallbackStillRequiresAuthentication()
    {
        var options = new AuthorizationOptions { FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build() };
        var provider = new PermissionPolicyProvider(Options.Create(options));

        Assert.Null(await provider.GetPolicyAsync("khong-ton-tai"));
        Assert.NotNull(await provider.GetFallbackPolicyAsync());
    }

    [Fact]
    public async Task Handler_Succeeds_WhenUserHasPermission()
    {
        var context = Context(Access(false, true, Permissions.Users.Read), Permissions.Users.Read);

        await Handle(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_Fails_WhenPermissionMissing()
    {
        var context = Context(Access(false, true, Permissions.Users.Create), Permissions.Users.Read);

        await Handle(context);

        Assert.False(context.HasSucceeded);
        Assert.Empty(context.FailureReasons);
    }

    [Fact]
    public async Task Handler_MustChangePassword_FailsWithReason_EvenWithPermission()
    {
        var context = Context(Access(true, true, Permissions.Users.Read), Permissions.Users.Read);

        await Handle(context);

        Assert.False(context.HasSucceeded);
        Assert.Contains(context.FailureReasons, r => r.Message == PermissionAuthorizationHandler.PasswordChangeRequiredReason);
    }

    [Fact]
    public async Task Handler_MustChangePassword_RouteAllowingIt_Succeeds()
    {
        var context = Context(Access(true, true, Permissions.Users.Read), Permissions.Users.Read, allowWhileMustChange: true);

        await Handle(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task Handler_InactiveUser_FailsAsUnauthenticated_EvenWithPermission()
    {
        var context = Context(Access(false, false, Permissions.Users.Read), Permissions.Users.Read);

        await Handle(context);

        Assert.False(context.HasSucceeded);
        Assert.Contains(context.FailureReasons,
            r => r.Message == PermissionAuthorizationHandler.UnauthenticatedReason && r.Handler is PermissionAuthorizationHandler);
    }

    [Fact]
    public async Task Handler_UnknownUser_FailsAsUnauthenticated()
    {
        var context = Context(null, Permissions.Users.Read);

        await Handle(context);

        Assert.False(context.HasSucceeded);
        Assert.Contains(context.FailureReasons, r => r.Message == PermissionAuthorizationHandler.UnauthenticatedReason);
    }
}

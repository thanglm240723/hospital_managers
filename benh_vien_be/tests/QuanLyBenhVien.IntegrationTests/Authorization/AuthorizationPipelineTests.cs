using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Presentation.Auth;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Authorization;

/// Route chỉ có trong test host để chứng minh policy quyền (chưa có endpoint production dùng RequirePermission).
public sealed class PermissionProbeModule : ICarterModule
{
    public const string Protected = "/api/v1/test-only/perm-probe";
    public const string NoMetadata = "/api/v1/test-only/no-metadata-probe";
    public const string Anonymous = "/api/v1/test-only/anonymous-probe";
    public const string AllowMustChange = "/api/v1/test-only/allow-must-change-probe";
    public static int Hits;

    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost(Protected, () => { Interlocked.Increment(ref Hits); return Results.Ok(); })
            .RequirePermission(Permissions.Users.Read);
        app.MapGet(NoMetadata, () => { Interlocked.Increment(ref Hits); return Results.Ok(); });
        app.MapGet(Anonymous, () => Results.Ok()).AllowAnonymous();
        app.MapGet(AllowMustChange, () => Results.Ok()).RequirePermission(Permissions.Users.Read).AllowWhilePasswordChangeRequired();
    }
}

public sealed class ThrowingAuditWriter : IAuditWriter
{
    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)
        => throw new InvalidOperationException("audit down");
}

[Collection(IntegrationCollection.Name)]
public class AuthorizationPipelineTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public AuthorizationPipelineTests(ContainersFixture containers) => _containers = containers;

    private static void AddProbe(IServiceCollection services) => services.AddSingleton<ICarterModule, PermissionProbeModule>();

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers, configureServices: AddProbe);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid UserId, AuthTestClient Client)> UserAsync(bool mustChangePassword = false, params string[] roles)
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email, mustChangePassword: mustChangePassword, roleCodes: roles);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (userId, client);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient())
            .SendAsync(HttpMethod.Post, PermissionProbeModule.Protected);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(response));
    }

    [Fact]
    public async Task MissingPermission_Returns403AndIsAudited_WithoutSecrets()
    {
        var (userId, client) = await UserAsync();
        var before = PermissionProbeModule.Hits;

        var response = await client.SendAsync(HttpMethod.Post, PermissionProbeModule.Protected,
            new { password = "Sup3r-Secret-Body" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await CodeAsync(response));
        Assert.Equal(before, PermissionProbeModule.Hits);
        var record = await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.SingleAsync(a => a.Action == AuditActions.AuthorizationDenied && a.ActorId == userId));
        Assert.Equal(AuditResult.Denied, record.Result);
        Assert.Contains(Permissions.Users.Read, record.Metadata);
        Assert.Contains(PermissionProbeModule.Protected, record.Metadata);
        Assert.DoesNotContain("Sup3r-Secret-Body", record.Metadata);
        Assert.DoesNotContain(client.AccessToken!, record.Metadata);
    }

    [Fact]
    public async Task AuditWriteFails_RequestIsNotExecuted()
    {
        var (_, client) = await UserAsync();
        await using var broken = _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IAuditWriter, ThrowingAuditWriter>())));
        var brokenClient = client.CloneWith(broken.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        var before = PermissionProbeModule.Hits;

        var response = await brokenClient.SendAsync(HttpMethod.Post, PermissionProbeModule.Protected);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before, PermissionProbeModule.Hits);
    }

    [Fact]
    public async Task Admin_PassesPermissionPolicy()
    {
        var admin = await _factory.LoginAsAdminAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Post, PermissionProbeModule.Protected)).StatusCode);
    }

    [Fact]
    public async Task MustChangePassword_BlocksPermissionRouteEvenForAdmin_ButNotMe()
    {
        var (_, client) = await UserAsync(mustChangePassword: true, SystemRoles.Admin);
        var before = PermissionProbeModule.Hits;

        var blocked = await client.SendAsync(HttpMethod.Post, PermissionProbeModule.Protected);
        var me = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("password_change_required", await CodeAsync(blocked));
        Assert.Equal(before, PermissionProbeModule.Hits);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task MustChangePassword_RouteMarkedAllow_PassesPolicy()
    {
        var (_, client) = await UserAsync(mustChangePassword: true, SystemRoles.Admin);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(PermissionProbeModule.AllowMustChange)).StatusCode);
    }

    [Fact]
    public async Task EndpointWithoutPermissionMetadata_StillRequiresLogin()
    {
        var anonymous = await new AuthTestClient(_factory.CreateHttpsClient()).GetAsync(PermissionProbeModule.NoMetadata);
        var (_, client) = await UserAsync();
        var loggedIn = await client.GetAsync(PermissionProbeModule.NoMetadata);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
    }

    [Fact]
    public async Task AnonymousRoutes_AreNotBlockedByPolicy()
    {
        var http = _factory.CreateHttpsClient();

        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync(PermissionProbeModule.Anonymous)).StatusCode);
    }

    [Fact]
    public async Task RedisDown_AuthorizedRequestsStillWork()
    {
        // Spec D11: Redis sập thì chậm hơn nhưng vẫn chạy (rơi về DB).
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" }, AddProbe);
        var admin = await factory.LoginAsAdminAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.SendAsync(HttpMethod.Post, PermissionProbeModule.Protected)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/auth/me")).StatusCode);
    }
}

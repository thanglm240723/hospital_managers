using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Authorization;

[Collection(IntegrationCollection.Name)]
public class AuthorizationPipelineTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public AuthorizationPipelineTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

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
        var response = await new AuthTestClient(_factory.CreateHttpsClient()).GetAsync("/api/v1/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(response));
    }

    [Fact]
    public async Task MissingPermission_Returns403AndIsAudited()
    {
        var (userId, client) = await UserAsync();

        var response = await client.GetAsync("/api/v1/permissions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await CodeAsync(response));
        var record = await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.SingleAsync(a => a.Action == AuditActions.AuthorizationDenied && a.ActorId == userId));
        Assert.Contains(Permissions.Catalog.Read, record.Metadata);
    }

    [Fact]
    public async Task Admin_GetsPermissionCatalog()
    {
        var admin = await _factory.LoginAsAdminAsync();

        var body = await (await admin.GetAsync("/api/v1/permissions")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(Permissions.All.Count, body.GetArrayLength());
    }

    [Fact]
    public async Task MustChangePassword_BlocksOtherEndpointsButNotMe()
    {
        var (_, client) = await UserAsync(mustChangePassword: true, SystemRoles.Admin);

        var blocked = await client.GetAsync("/api/v1/permissions");
        var me = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("password_change_required", await CodeAsync(blocked));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Sessions_IsBlockedWhilePasswordChangeRequired()
    {
        var (_, client) = await UserAsync(mustChangePassword: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/auth/sessions")).StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous()
        => Assert.Equal(HttpStatusCode.OK, (await _factory.CreateHttpsClient().GetAsync("/health")).StatusCode);

    [Fact]
    public async Task RedisDown_LoginRefreshAndAuthorizedRequestsStillWork()
    {
        // Spec D11: Redis sập ⇒ hệ thống chậm hơn nhưng vẫn chạy (mỗi thao tác Redis chờ hết timeout 1s rồi rơi về DB).
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var admin = await factory.LoginAsAdminAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/auth/me")).StatusCode);
    }
}

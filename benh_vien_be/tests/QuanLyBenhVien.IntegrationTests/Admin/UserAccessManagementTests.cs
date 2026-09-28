using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class UserAccessManagementTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public UserAccessManagementTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Id, AuthTestClient Client)> StaffAsync()
    {
        var email = TestData.NewEmail("staff");
        var id = await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (id, client);
    }

    private Task<Guid> RoleIdAsync(string code)
        => TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync());

    private Task<Guid> SeededAdminIdAsync()
        => TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Email == _factory.AdminEmail).Select(u => u.Id).SingleAsync());

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task GrantThenRevoke_TakesEffectOnTheVeryNextRequest()
    {
        var (staffId, staff) = await StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/permissions")).StatusCode);   // cache đã ấm

        (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{staffId}/permissions/grant",
            new { permissionCode = Permissions.Catalog.Read, reason = "Hỗ trợ cấu hình" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/permissions")).StatusCode);

        (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{staffId}/permissions/revoke",
            new { permissionCode = Permissions.Catalog.Read, reason = "Hết nhu cầu" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/permissions")).StatusCode);

        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.UserPermissionGrant && a.ResourceId == staffId.ToString())));
    }

    [Fact]
    public async Task SetRoles_TakesEffectOnTheVeryNextRequest()
    {
        var (staffId, staff) = await StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/users")).StatusCode);

        var response = await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{staffId}/roles",
            new { roleIds = new[] { await RoleIdAsync(SystemRoles.Admin) } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task RemovingOwnAdminRole_Returns409()
    {
        var response = await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{await SeededAdminIdAsync()}/roles",
            new { roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("self_action_forbidden", await CodeAsync(response));
    }

    [Fact]
    public async Task RemovingLastAdminRole_Returns409()
    {
        var (operatorId, operatorClient) = await StaffAsync();
        await TestData.GrantAsync(_factory, operatorId, Permissions.Users.ManageRoles);

        var response = await operatorClient.SendAsync(HttpMethod.Put, $"/api/v1/users/{await SeededAdminIdAsync()}/roles",
            new { roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last_admin", await CodeAsync(response));
    }

    [Fact]
    public async Task UnknownPermissionOrRole_Returns400()
    {
        var (staffId, _) = await StaffAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{staffId}/permissions/grant",
            new { permissionCode = "patients.read", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{staffId}/roles",
            new { roleIds = new[] { Guid.NewGuid() } })).StatusCode);
    }

    /// Bảo vệ bất biến "còn ≥ 1 admin" khi 2 admin duy nhất cùng gỡ role admin của nhau đồng thời qua
    /// PUT .../roles — guard trong SetUserRolesCommandHandler phải tuần tự hoá bằng pg_advisory_xact_lock
    /// giống Deactivate, nếu không cả hai có thể cùng đếm thấy ≥ 1 admin khác và cùng đi qua.
    [Fact]
    public async Task SetRoles_TwoLastActiveAdminsRemoveEachOtherConcurrently_ExactlyOneSucceedsOtherGetsLastAdmin409()
    {
        var admin1Id = await SeededAdminIdAsync();
        var admin2Email = TestData.NewEmail("admin2");
        var admin2Id = await TestData.CreateUserAsync(_factory, admin2Email, TestData.DefaultPassword, false, true, SystemRoles.Admin);
        var admin2 = new AuthTestClient(_factory.CreateHttpsClient());
        (await admin2.LoginAsync(admin2Email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        var responses = await Task.WhenAll(
            _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{admin2Id}/roles", new { roleIds = Array.Empty<Guid>() }),
            admin2.SendAsync(HttpMethod.Put, $"/api/v1/users/{admin1Id}/roles", new { roleIds = Array.Empty<Guid>() }));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.NoContent);
        var conflict = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("last_admin", await CodeAsync(conflict));

        var adminRoleId = await RoleIdAsync(SystemRoles.Admin);
        var stillAdminCount = await TestData.QueryAsync(_factory, db => db.Users
            .Where(u => u.Id == admin1Id || u.Id == admin2Id)
            .CountAsync(u => u.RoleAssignments.Any(r => r.RoleId == adminRoleId)));
        Assert.Equal(1, stillAdminCount);
    }
}

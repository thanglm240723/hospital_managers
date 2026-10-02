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

    private async Task<(Guid Id, AuthTestClient Client)> StaffAsync(params string[] roles)
    {
        var email = TestData.NewEmail("staff");
        var id = await TestData.CreateUserAsync(_factory, email, TestData.DefaultPassword, false, true, roles);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (id, client);
    }

    private Task<Guid> RoleIdAsync(string code)
        => TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync());

    private Task<Guid> SeededAdminIdAsync()
        => TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Email == _factory.AdminEmail).Select(u => u.Id).SingleAsync());

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await JsonAsync(response)).GetProperty("code").GetString();

    private async Task<uint> VersionAsync(Guid id)
        => (await JsonAsync(await _admin.GetAsync($"/api/v1/users/{id}"))).GetProperty("rowVersion").GetUInt32();

    private static Dictionary<string, string>? IfMatch(uint? version)
        => version is null ? null : new Dictionary<string, string> { ["If-Match"] = $"\"{version}\"" };

    private Task<HttpResponseMessage> SetRolesAsync(AuthTestClient client, Guid id, uint? version, params Guid[] roleIds)
        => client.SendAsync(HttpMethod.Put, $"/api/v1/users/{id}/roles", new { roleIds }, extraHeaders: IfMatch(version));

    private Task<HttpResponseMessage> GrantAsync(Guid id, string code, string reason, uint? version, string verb = "grant")
        => _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/permissions/{verb}", new { permissionCode = code, reason }, extraHeaders: IfMatch(version));

    [Fact]
    public async Task GrantThenRevoke_TakesEffectOnTheVeryNextRequest()
    {
        var (staffId, staff) = await StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/permissions")).StatusCode);   // cache đã ấm

        var granted = await GrantAsync(staffId, Permissions.Catalog.Read, "Hỗ trợ cấu hình", await VersionAsync(staffId));
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        var grantedDto = await JsonAsync(granted);
        Assert.Contains(Permissions.Catalog.Read, grantedDto.GetProperty("effectivePermissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/permissions")).StatusCode);

        var revoked = await GrantAsync(staffId, Permissions.Catalog.Read, "Hết nhu cầu", grantedDto.GetProperty("rowVersion").GetUInt32(), "revoke");
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/permissions")).StatusCode);

        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.UserPermissionGrant && a.ResourceId == staffId.ToString())));
        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.UserPermissionRevoke && a.ResourceId == staffId.ToString())));
    }

    [Fact]
    public async Task Grant_AlreadyGranted_And_RevokeMissing_AreIdempotentNoChange()
    {
        var (staffId, _) = await StaffAsync();
        var v1 = (await JsonAsync(await GrantAsync(staffId, Permissions.Catalog.Read, "lần 1", await VersionAsync(staffId)))).GetProperty("rowVersion").GetUInt32();

        var again = await GrantAsync(staffId, Permissions.Catalog.Read, "lần 2", v1);
        Assert.Equal(HttpStatusCode.OK, again.StatusCode);
        var dto = await JsonAsync(again);
        Assert.Equal(v1, dto.GetProperty("rowVersion").GetUInt32());
        Assert.Equal("lần 1", dto.GetProperty("permissionGrants")[0].GetProperty("reason").GetString());

        var missing = await GrantAsync(staffId, Permissions.Users.Read, "không có", v1, "revoke");
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Equal(v1, (await JsonAsync(missing)).GetProperty("rowVersion").GetUInt32());
    }

    [Fact]
    public async Task Revoke_DoesNotRemovePermissionComingFromRole()
    {
        var (staffId, staff) = await StaffAsync(SystemRoles.Admin);
        var v = (await JsonAsync(await GrantAsync(staffId, Permissions.Users.Read, "trùng role", await VersionAsync(staffId)))).GetProperty("rowVersion").GetUInt32();

        var revoked = await GrantAsync(staffId, Permissions.Users.Read, "gỡ lẻ", v, "revoke");

        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        var dto = await JsonAsync(revoked);
        Assert.Equal(0, dto.GetProperty("permissionGrants").GetArrayLength());
        Assert.Contains(Permissions.Users.Read, dto.GetProperty("effectivePermissions").EnumerateArray().Select(p => p.GetString()));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task Grant_InvalidInput_Returns400_IfMatchMissing400_Stale412()
    {
        var (staffId, _) = await StaffAsync();
        var version = await VersionAsync(staffId);

        Assert.Equal(HttpStatusCode.BadRequest, (await GrantAsync(staffId, "patients.read", "x", version)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GrantAsync(staffId, Permissions.Catalog.Read, "  ", version)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await GrantAsync(staffId, Permissions.Catalog.Read, "ok", null)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await GrantAsync(staffId, Permissions.Catalog.Read, "ok", version + 1000)).StatusCode);
        Assert.Equal(version, await VersionAsync(staffId));
    }

    [Fact]
    public async Task SetRoles_TakesEffectOnTheVeryNextRequest_AndIsAudited()
    {
        var (staffId, staff) = await StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/users")).StatusCode);

        var response = await SetRolesAsync(_admin, staffId, await VersionAsync(staffId), await RoleIdAsync(SystemRoles.Admin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(SystemRoles.Admin, (await JsonAsync(response)).GetProperty("roles")[0].GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/users")).StatusCode);
        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.UserSetRoles && a.ResourceId == staffId.ToString())));
    }

    [Fact]
    public async Task SetRoles_ReplacesWholeSet()
    {
        var (staffId, _) = await StaffAsync(SystemRoles.Doctor);
        var cashier = await RoleIdAsync(SystemRoles.Cashier);

        var response = await SetRolesAsync(_admin, staffId, await VersionAsync(staffId), cashier);

        var roles = (await JsonAsync(response)).GetProperty("roles").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToList();
        Assert.Equal([SystemRoles.Cashier], roles);
    }

    [Fact]
    public async Task SetRoles_StaleOrMissingIfMatch()
    {
        var (staffId, _) = await StaffAsync();
        var version = await VersionAsync(staffId);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetRolesAsync(_admin, staffId, null)).StatusCode);
        Assert.Equal(HttpStatusCode.PreconditionFailed, (await SetRolesAsync(_admin, staffId, version + 1000)).StatusCode);
    }

    [Fact]
    public async Task RemovingOwnAdminRole_Returns409()
    {
        var adminId = await SeededAdminIdAsync();
        var response = await SetRolesAsync(_admin, adminId, await VersionAsync(adminId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("self_action_forbidden", await CodeAsync(response));
    }

    [Fact]
    public async Task RemovingLastAdminRole_Returns409()
    {
        var (operatorId, operatorClient) = await StaffAsync();
        await TestData.GrantAsync(_factory, operatorId, Permissions.Users.ManageRoles, Permissions.Users.Read);
        var adminId = await SeededAdminIdAsync();

        var response = await SetRolesAsync(operatorClient, adminId, await VersionAsync(adminId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last_admin", await CodeAsync(response));
    }

    [Fact]
    public async Task UnknownOrDuplicateRole_Returns400()
    {
        var (staffId, _) = await StaffAsync();
        var version = await VersionAsync(staffId);
        var doctor = await RoleIdAsync(SystemRoles.Doctor);

        Assert.Equal(HttpStatusCode.BadRequest, (await SetRolesAsync(_admin, staffId, version, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await SetRolesAsync(_admin, staffId, version, doctor, doctor)).StatusCode);
        Assert.Equal(version, await VersionAsync(staffId));
    }
}

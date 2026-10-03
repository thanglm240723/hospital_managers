using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class RolesAdminTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public RolesAdminTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task List_ContainsNineSystemRoles()
    {
        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));

        Assert.Equal(9, roles.EnumerateArray().Count(r => r.GetProperty("isSystem").GetBoolean()));
    }

    [Fact]
    public async Task List_ReturnsDbRolesWithRealIdsCatalogPermissionsAndRowVersion()
    {
        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));
        var dbIds = await TestData.QueryAsync(_factory, db => db.Roles.Select(r => r.Id).ToListAsync());
        var catalog = Permissions.All.Select(p => p.Code).ToHashSet();

        Assert.Equal(dbIds.Count, roles.GetArrayLength());
        foreach (var role in roles.EnumerateArray())
        {
            var id = role.GetProperty("id").GetGuid();
            Assert.Contains(id, dbIds);
            Assert.NotEqual(Guid.Empty, id);
            Assert.True(role.GetProperty("rowVersion").GetUInt32() > 0);
            Assert.All(role.GetProperty("permissionCodes").EnumerateArray(), c => Assert.Contains(c.GetString()!, catalog));
            Assert.False(role.TryGetProperty("passwordHash", out _));
        }

        var admin = roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == SystemRoles.Admin);
        Assert.Equal(Permissions.All.Count, admin.GetProperty("permissionCodes").GetArrayLength());
    }

    [Fact]
    public async Task List_Returns401WithoutLoginAnd403WithoutRolesRead()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().GetAsync("/api/v1/roles")).StatusCode);

        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var noPerm = new AuthTestClient(_factory.CreateHttpsClient());
        (await noPerm.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await noPerm.GetAsync("/api/v1/roles")).StatusCode);
    }
}

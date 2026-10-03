using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class PermissionCatalogTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public PermissionCatalogTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Admin_GetsCatalogMatchingCodeAndDb()
    {
        var admin = await _factory.LoginAsAdminAsync();

        var response = await admin.GetAsync("/api/v1/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().ToList();
        var codes = items.Select(i => i.GetProperty("code").GetString()!).ToHashSet();
        var dbCodes = await TestData.QueryAsync(_factory, db => db.Permissions.Select(p => p.Id).ToListAsync());
        Assert.Equal(dbCodes.ToHashSet(), codes);
        Assert.All(codes, c => Assert.True(Permissions.IsDefined(c)));
        Assert.All(items, i =>
        {
            Assert.False(string.IsNullOrEmpty(i.GetProperty("group").GetString()));
            Assert.False(string.IsNullOrEmpty(i.GetProperty("description").GetString()));
        });
    }

    [Fact]
    public async Task Anonymous_Gets401_AndUserWithoutPermission_Gets403()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().GetAsync("/api/v1/permissions")).StatusCode);

        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/permissions")).StatusCode);
    }
}

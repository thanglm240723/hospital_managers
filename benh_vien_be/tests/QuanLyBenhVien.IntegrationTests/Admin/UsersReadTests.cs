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
public class UsersReadTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public UsersReadTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private Task<Guid> RoleIdAsync(string code)
        => TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync());

    [Theory]
    [InlineData("pageNumber=0")]
    [InlineData("pageSize=0")]
    [InlineData("pageSize=101")]
    public async Task List_InvalidPaging_Returns400(string query)
        => Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync($"/api/v1/users?{query}")).StatusCode);

    [Fact]
    public async Task List_InvalidStatus_Returns400()
        => Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/users?status=bogus")).StatusCode);

    [Fact]
    public async Task List_Returns401WithoutLoginAnd403WithoutUsersRead()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().GetAsync("/api/v1/users")).StatusCode);

        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var noPerm = new AuthTestClient(_factory.CreateHttpsClient());
        (await noPerm.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Forbidden, (await noPerm.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await noPerm.GetAsync($"/api/v1/users/{Guid.NewGuid()}")).StatusCode);
    }

    [Fact]
    public async Task List_CountsPagesAndFiltersCorrectly_PastLastPageIsEmpty_NoSensitiveFields()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 5; i++)
            await TestData.CreateUserAsync(_factory, $"read-{tag}-{i}@test.local", roleCodes: SystemRoles.Doctor);

        var page1 = await JsonAsync(await _admin.GetAsync($"/api/v1/users?searchTerm=read-{tag}&pageSize=2&pageNumber=1"));
        var page3 = await JsonAsync(await _admin.GetAsync($"/api/v1/users?searchTerm=read-{tag}&pageSize=2&pageNumber=3"));
        var past = await JsonAsync(await _admin.GetAsync($"/api/v1/users?searchTerm=read-{tag}&pageSize=2&pageNumber=9"));

        Assert.Equal(5, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, page1.GetProperty("totalPages").GetInt32());
        Assert.Equal(1, page1.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, page1.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(1, page3.GetProperty("items").GetArrayLength());
        Assert.Equal(0, past.GetProperty("items").GetArrayLength());
        Assert.Equal(5, past.GetProperty("totalCount").GetInt32());

        var item = page1.GetProperty("items")[0];
        Assert.Equal("doctor", item.GetProperty("roles")[0].GetProperty("code").GetString());
        Assert.NotEqual(Guid.Empty, item.GetProperty("roles")[0].GetProperty("id").GetGuid());
        Assert.True(item.GetProperty("rowVersion").GetUInt32() > 0);
        foreach (var name in new[] { "passwordHash", "securityVersion", "refreshToken" })
            Assert.False(item.TryGetProperty(name, out _));
    }

    [Fact]
    public async Task List_HugePageNumber_ReturnsEmptyItemsNot500()
    {
        var response = await _admin.GetAsync("/api/v1/users?pageNumber=2147483647&pageSize=100");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await JsonAsync(response);
        Assert.Equal(0, page.GetProperty("items").GetArrayLength());
        Assert.True(page.GetProperty("totalCount").GetInt32() >= 1);
    }

    [Fact]
    public async Task List_FilterByRoleIdAndStatus()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        await TestData.CreateUserAsync(_factory, $"flt-{tag}-a@test.local", roleCodes: SystemRoles.Doctor);
        await TestData.CreateUserAsync(_factory, $"flt-{tag}-b@test.local", mustChangePassword: true, roleCodes: SystemRoles.Doctor);
        await TestData.CreateUserAsync(_factory, $"flt-{tag}-c@test.local", isActive: false);
        var doctorId = await RoleIdAsync(SystemRoles.Doctor);

        async Task<int> CountAsync(string q)
            => (await JsonAsync(await _admin.GetAsync($"/api/v1/users?searchTerm=flt-{tag}&{q}"))).GetProperty("totalCount").GetInt32();

        Assert.Equal(3, await CountAsync(""));
        Assert.Equal(2, await CountAsync($"roleId={doctorId}"));
        Assert.Equal(1, await CountAsync("status=active"));
        Assert.Equal(1, await CountAsync("status=must_change_password"));
        Assert.Equal(1, await CountAsync("status=locked"));
        Assert.Equal(1, await CountAsync($"roleId={doctorId}&status=must_change_password"));
    }

    [Fact]
    public async Task List_StableSortWithDuplicateNames_NoOverlapBetweenPages()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 4; i++)
            await TestData.CreateUserAsync(_factory, $"dup-{tag}-{i}@test.local");   // cùng FullName "Test User"

        var ids = new List<string>();
        for (var p = 1; p <= 2; p++)
        {
            var page = await JsonAsync(await _admin.GetAsync($"/api/v1/users?searchTerm=dup-{tag}&pageSize=2&pageNumber={p}"));
            ids.AddRange(page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()!));
        }

        Assert.Equal(4, ids.Distinct().Count());
        Assert.Equal(ids.Order(StringComparer.Ordinal).ToList(), ids);   // cùng tên → theo Id tăng dần (UUIDv7)
    }

    [Fact]
    public async Task Get_ReturnsDetailWithGrantsAndEffectivePermissions_NoSensitiveFields()
    {
        var email = TestData.NewEmail();
        var id = await TestData.CreateUserAsync(_factory, email, roleCodes: SystemRoles.Doctor);
        await TestData.GrantAsync(_factory, id, Permissions.Roles.Read);

        var detail = await JsonAsync(await _admin.GetAsync($"/api/v1/users/{id}"));

        Assert.Equal(email, detail.GetProperty("email").GetString());
        Assert.Equal(Permissions.Roles.Read, detail.GetProperty("permissionGrants")[0].GetProperty("code").GetString());
        Assert.Equal("test setup", detail.GetProperty("permissionGrants")[0].GetProperty("reason").GetString());
        Assert.Contains(detail.GetProperty("effectivePermissions").EnumerateArray(), p => p.GetString() == Permissions.Roles.Read);
        Assert.True(detail.GetProperty("rowVersion").GetUInt32() > 0);
        foreach (var name in new[] { "passwordHash", "securityVersion", "refreshToken" })
            Assert.False(detail.TryGetProperty(name, out _));
    }

    [Fact]
    public async Task Get_UnknownUser_Returns404()
    {
        var response = await _admin.GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("user_not_found", (await JsonAsync(response)).GetProperty("code").GetString());
    }
}

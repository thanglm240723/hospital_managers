using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Admin;

/// Bất biến "luôn còn ≥ 1 admin đang hoạt động" dưới tranh chấp: hai admin duy nhất cùng khoá/gỡ admin của nhau.
/// Guard phải tuần tự hoá bằng pg_advisory_xact_lock; nếu không cả hai cùng đếm thấy ≥ 1 admin khác và cùng qua.
[Collection(IntegrationCollection.Name)]
public class LastAdminConcurrencyTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin1 = default!;
    private Guid _admin1Id;

    public LastAdminConcurrencyTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin1 = await _factory.LoginAsAdminAsync();
        _admin1Id = await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Email == _factory.AdminEmail).Select(u => u.Id).SingleAsync());
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Id, AuthTestClient Client)> SecondAdminAsync()
    {
        var email = TestData.NewEmail("admin2");
        var id = await TestData.CreateUserAsync(_factory, email, TestData.DefaultPassword, false, true, SystemRoles.Admin);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (id, client);
    }

    private async Task<uint> VersionAsync(Guid id)
        => (await (await _admin1.GetAsync($"/api/v1/users/{id}")).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rowVersion").GetUInt32();

    private static Task<HttpResponseMessage> RemoveAllRolesAsync(AuthTestClient client, Guid id, uint version)
        => client.SendAsync(HttpMethod.Put, $"/api/v1/users/{id}/roles", new { roleIds = Array.Empty<Guid>() },
            extraHeaders: new Dictionary<string, string> { ["If-Match"] = $"\"{version}\"" });

    private Task<int> ActiveAdminCountAsync(Guid a, Guid b)
        => TestData.QueryAsync(_factory, db => db.Users
            .Where(u => (u.Id == a || u.Id == b) && u.IsActive)
            .CountAsync(u => u.RoleAssignments.Any(r => db.Roles.Any(role => role.Id == r.RoleId && role.Code == SystemRoles.Admin))));

    private static async Task AssertOneWinsOtherLastAdmin(HttpResponseMessage[] responses)
    {
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var conflict = Assert.Single(responses, r => r.StatusCode != HttpStatusCode.OK);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("last_admin", (await conflict.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deactivate_TwoLastActiveAdminsConcurrently_ExactlyOneSucceeds()
    {
        var (admin2Id, admin2) = await SecondAdminAsync();

        var responses = await Task.WhenAll(
            _admin1.SendAsync(HttpMethod.Post, $"/api/v1/users/{admin2Id}/deactivate"),
            admin2.SendAsync(HttpMethod.Post, $"/api/v1/users/{_admin1Id}/deactivate"));

        await AssertOneWinsOtherLastAdmin(responses);
        Assert.Equal(1, await ActiveAdminCountAsync(_admin1Id, admin2Id));
    }

    [Fact]
    public async Task SetRoles_TwoLastActiveAdminsRemoveEachOtherConcurrently_ExactlyOneSucceeds()
    {
        var (admin2Id, admin2) = await SecondAdminAsync();
        var v1 = await VersionAsync(_admin1Id);
        var v2 = await VersionAsync(admin2Id);

        var responses = await Task.WhenAll(
            RemoveAllRolesAsync(_admin1, admin2Id, v2),
            RemoveAllRolesAsync(admin2, _admin1Id, v1));

        await AssertOneWinsOtherLastAdmin(responses);
        Assert.Equal(1, await ActiveAdminCountAsync(_admin1Id, admin2Id));
    }

    [Fact]
    public async Task DeactivateAndRemoveRole_Concurrently_StillLeavesOneActiveAdmin()
    {
        var (admin2Id, admin2) = await SecondAdminAsync();
        var v1 = await VersionAsync(_admin1Id);

        var responses = await Task.WhenAll(
            _admin1.SendAsync(HttpMethod.Post, $"/api/v1/users/{admin2Id}/deactivate"),
            RemoveAllRolesAsync(admin2, _admin1Id, v1));

        // Hai actor đối xứng: nếu kẻ thắng commit trước khi request của kẻ thua qua auth, kẻ thua mất phiên/quyền
        // (401/403) thay vì nhận 409 last_admin — bất biến vẫn giữ.
        var diagnostics = string.Join(", ", await Task.WhenAll(responses.Select(async r =>
            $"{(int)r.StatusCode} {await r.Content.ReadAsStringAsync()}")));
        Assert.True(responses.Count(r => r.StatusCode == HttpStatusCode.OK) == 1, diagnostics);
        var loser = responses.Single(r => r.StatusCode != HttpStatusCode.OK);
        if (loser.StatusCode == HttpStatusCode.Conflict)
        {
            Assert.True((await loser.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString() == "last_admin", diagnostics);
        }
        else
        {
            Assert.True(loser.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden, diagnostics);
        }

        Assert.Equal(1, await ActiveAdminCountAsync(_admin1Id, admin2Id));
    }
}

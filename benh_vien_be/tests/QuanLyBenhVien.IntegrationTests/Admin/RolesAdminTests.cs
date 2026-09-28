using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
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

    private async Task<HttpResponseMessage> CreateRoleAsync(string code, params string[] permissions)
        => await _admin.SendAsync(HttpMethod.Post, "/api/v1/roles", new { code, name = "Kiểm toán", permissionCodes = permissions });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task List_ContainsNineSystemRoles()
    {
        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));

        Assert.Equal(9, roles.EnumerateArray().Count(r => r.GetProperty("isSystem").GetBoolean()));
    }

    [Fact]
    public async Task Create_ValidatesCodeAndRejectsDuplicates()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("Not Kebab")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("auditor-x", "patients.read")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateRoleAsync("auditor")).StatusCode);

        var duplicate = await CreateRoleAsync("auditor");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("conflict", (await JsonAsync(duplicate)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rename_ChangesName()
    {
        var id = (await JsonAsync(await CreateRoleAsync("renamer"))).GetProperty("id").GetString();

        (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{id}", new { name = "Tên mới" })).EnsureSuccessStatusCode();

        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));
        Assert.Equal("Tên mới", roles.EnumerateArray().Single(r => r.GetProperty("id").GetString() == id).GetProperty("name").GetString());
    }

    [Fact]
    public async Task SetRolePermissions_AffectsEveryMemberOnNextRequest()
    {
        var roleId = (await JsonAsync(await CreateRoleAsync("catalog-viewer"))).GetProperty("id").GetGuid();
        var email = TestData.NewEmail();
        var memberId = await TestData.CreateUserAsync(_factory, email);
        (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{memberId}/roles", new { roleIds = new[] { roleId } })).EnsureSuccessStatusCode();
        var member = new AuthTestClient(_factory.CreateHttpsClient());
        (await member.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/permissions")).StatusCode);

        (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{roleId}/permissions",
            new { permissionCodes = new[] { Permissions.Catalog.Read } })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/permissions")).StatusCode);
    }

    [Fact]
    public async Task SetRolePermissions_RejectsStrippingIdentityAccessFromAdminRole()
    {
        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));
        var adminRoleId = roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == SystemRoles.Admin).GetProperty("id").GetString();

        var stripped = await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{adminRoleId}/permissions", new { permissionCodes = Array.Empty<string>() });

        Assert.Equal(HttpStatusCode.Conflict, stripped.StatusCode);
        Assert.Equal("conflict", (await JsonAsync(stripped)).GetProperty("code").GetString());

        var missingOne = await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{adminRoleId}/permissions",
            new { permissionCodes = Permissions.IdentityAccess.Select(p => p.Code).Skip(1).ToArray() });

        Assert.Equal(HttpStatusCode.Conflict, missingOne.StatusCode);

        var kept = await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{adminRoleId}/permissions",
            new { permissionCodes = Permissions.IdentityAccess.Select(p => p.Code).ToArray() });

        Assert.Equal(HttpStatusCode.NoContent, kept.StatusCode);
    }

    /// I2 (final review): SetRolePermissions phải khoá tư vấn (chung với SetUserRoles) VÔ ĐIỀU KIỆN rồi mới
    /// đọc danh sách holder, nếu không một SetUserRoles thêm user vào role đang chạy song song có thể lọt
    /// khỏi vòng invalidate — user giữ quyền đã bị gỡ vô thời hạn vì perm:{uid} không có TTL. Chạy nhiều
    /// vòng lặp với hai thứ tự khác nhau để tăng khả năng bắt được race nếu khoá không tuần tự hoá đúng.
    [Fact]
    public async Task SetRolePermissions_ConcurrentWithRoleAssignment_NeverLeavesStalePermission()
    {
        for (var i = 0; i < 8; i++)
        {
            var roleId = (await JsonAsync(await CreateRoleAsync($"catalog-viewer-{i}", Permissions.Catalog.Read))).GetProperty("id").GetGuid();
            var email = TestData.NewEmail();
            var memberId = await TestData.CreateUserAsync(_factory, email);
            var member = new AuthTestClient(_factory.CreateHttpsClient());
            (await member.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

            var responses = await Task.WhenAll(
                _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{memberId}/roles", new { roleIds = new[] { roleId } }),
                _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{roleId}/permissions", new { permissionCodes = Array.Empty<string>() }));

            Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

            // Trạng thái DB cuối cùng: member có role, role không còn quyền nào — perm:{uid} phải phản ánh
            // đúng điều đó ngay lần đọc kế tiếp, không được kẹt lại quyền cũ do bỏ lỡ invalidate.
            var permissionsResponse = await member.GetAsync("/api/v1/permissions");
            Assert.Equal(HttpStatusCode.Forbidden, permissionsResponse.StatusCode);
        }
    }
}

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
public class RolesCommandTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public RolesCommandTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<HttpResponseMessage> CreateRoleAsync(string code, string name = "Kiểm toán", params string[] permissions)
        => await _admin.SendAsync(HttpMethod.Post, "/api/v1/roles", new { code, name, permissionCodes = permissions });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private async Task<JsonElement> GetRoleAsync(Guid id)
    {
        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));
        return roles.EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == id);
    }

    private async Task<JsonElement> GetRoleByCodeAsync(string code)
    {
        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));
        return roles.EnumerateArray().Single(r => r.GetProperty("code").GetString() == code);
    }

    private Task<HttpResponseMessage> PutAsync(string path, object body, uint? version)
        => _admin.SendAsync(HttpMethod.Put, path, body,
            extraHeaders: version is null ? null : new Dictionary<string, string> { ["If-Match"] = $"\"{version}\"" });

    private async Task<(Guid Id, uint Version)> NewRoleAsync(string code, params string[] permissions)
    {
        var created = await JsonAsync(await CreateRoleAsync(code, "Vai trò thử", permissions));
        return (created.GetProperty("id").GetGuid(), created.GetProperty("rowVersion").GetUInt32());
    }

    [Fact]
    public async Task Create_ValidatesInputAndMapsUniqueViolationToRoleCodeTaken()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("Not Kebab")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("x")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("auditor-x", "Tên", "patients.read")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("auditor-y", "   ")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("auditor-z", new string('a', 101))).StatusCode);

        var created = await CreateRoleAsync("auditor", "Kiểm toán", Permissions.Catalog.Read);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await JsonAsync(created);
        Assert.Equal($"/api/v1/roles/{body.GetProperty("id").GetGuid()}", created.Headers.Location!.OriginalString);
        Assert.False(body.GetProperty("isSystem").GetBoolean());
        Assert.True(body.GetProperty("rowVersion").GetUInt32() > 0);

        var duplicate = await CreateRoleAsync("auditor");
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("role_code_taken", (await JsonAsync(duplicate)).GetProperty("code").GetString());

        var raceDuplicate = await Task.WhenAll(CreateRoleAsync("racer"), CreateRoleAsync("racer"));
        Assert.Single(raceDuplicate, r => r.StatusCode == HttpStatusCode.Created);
        var loser = raceDuplicate.Single(r => r.StatusCode != HttpStatusCode.Created);
        Assert.Equal(HttpStatusCode.Conflict, loser.StatusCode);
        Assert.Equal("role_code_taken", (await JsonAsync(loser)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rename_ChangesNameAndRowVersion_StaleOrMissingVersionRejected()
    {
        var (id, version) = await NewRoleAsync("renamer");

        var renamed = await PutAsync($"/api/v1/roles/{id}", new { name = "Tên mới" }, version);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var dto = await JsonAsync(renamed);
        Assert.Equal("Tên mới", dto.GetProperty("name").GetString());
        Assert.NotEqual(version, dto.GetProperty("rowVersion").GetUInt32());
        Assert.Equal(dto.GetProperty("rowVersion").GetUInt32(), (await GetRoleAsync(id)).GetProperty("rowVersion").GetUInt32());

        var stale = await PutAsync($"/api/v1/roles/{id}", new { name = "Cũ" }, version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("Tên mới", (await GetRoleAsync(id)).GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync($"/api/v1/roles/{id}", new { name = "X" }, null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{id}", new { name = "X" },
            extraHeaders: new Dictionary<string, string> { ["If-Match"] = "\"abc\"" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync($"/api/v1/roles/{id}", new { name = " " }, dto.GetProperty("rowVersion").GetUInt32())).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync($"/api/v1/roles/{Guid.NewGuid()}", new { name = "X" }, 1)).StatusCode);
    }

    [Fact]
    public async Task SetPermissions_OnlyPermissionChange_BumpsRowVersion_AndStaleIfMatchGets412()
    {
        var (id, version) = await NewRoleAsync("perm-bump");

        var updated = await PutAsync($"/api/v1/roles/{id}/permissions", new { permissionCodes = new[] { Permissions.Catalog.Read } }, version);
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        var dto = await JsonAsync(updated);
        var newVersion = dto.GetProperty("rowVersion").GetUInt32();
        Assert.NotEqual(version, newVersion);
        Assert.Equal(Permissions.Catalog.Read, dto.GetProperty("permissionCodes").EnumerateArray().Single().GetString());
        Assert.Equal(newVersion, (await GetRoleAsync(id)).GetProperty("rowVersion").GetUInt32());

        var stale = await PutAsync($"/api/v1/roles/{id}/permissions", new { permissionCodes = Array.Empty<string>() }, version);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Single((await GetRoleAsync(id)).GetProperty("permissionCodes").EnumerateArray());

        Assert.Equal(HttpStatusCode.BadRequest, (await PutAsync($"/api/v1/roles/{id}/permissions", new { permissionCodes = new[] { "nope.read" } }, newVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PutAsync($"/api/v1/roles/{Guid.NewGuid()}/permissions", new { permissionCodes = Array.Empty<string>() }, 1)).StatusCode);
    }

    [Fact]
    public async Task SetPermissions_AffectsEveryMemberOnNextRequest()
    {
        var (roleId, version) = await NewRoleAsync("catalog-viewer");
        var members = new List<AuthTestClient>();
        for (var i = 0; i < 2; i++)
        {
            var email = TestData.NewEmail();
            await TestData.CreateUserAsync(_factory, email, roleCodes: "catalog-viewer");
            var member = new AuthTestClient(_factory.CreateHttpsClient());
            (await member.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
            // Làm ấm cache quyền: request này nạp perm:{uid} (tập rỗng) vào Redis.
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/permissions")).StatusCode);
            members.Add(member);
        }

        var granted = await PutAsync($"/api/v1/roles/{roleId}/permissions", new { permissionCodes = new[] { Permissions.Catalog.Read } }, version);
        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        foreach (var member in members)
            Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/permissions")).StatusCode);

        var revoked = await PutAsync($"/api/v1/roles/{roleId}/permissions", new { permissionCodes = Array.Empty<string>() },
            (await JsonAsync(granted)).GetProperty("rowVersion").GetUInt32());
        Assert.Equal(HttpStatusCode.OK, revoked.StatusCode);
        foreach (var member in members)
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/permissions")).StatusCode);
    }

    [Fact]
    public async Task SetPermissions_RejectedOrUnchanged_WritesNoInvalidationAndNoChange()
    {
        var admin = await GetRoleByCodeAsync(SystemRoles.Admin);
        var adminId = admin.GetProperty("id").GetGuid();
        var version = admin.GetProperty("rowVersion").GetUInt32();
        var before = await TestData.QueryAsync(_factory, db => db.CacheInvalidations.CountAsync());

        var stripped = await PutAsync($"/api/v1/roles/{adminId}/permissions", new { permissionCodes = Array.Empty<string>() }, version);
        Assert.Equal(HttpStatusCode.Conflict, stripped.StatusCode);
        Assert.Equal("admin_core_permissions_required", (await JsonAsync(stripped)).GetProperty("code").GetString());

        var missingOne = await PutAsync($"/api/v1/roles/{adminId}/permissions",
            new { permissionCodes = Permissions.IdentityAccess.Select(p => p.Code).Skip(1).ToArray() }, version);
        Assert.Equal(HttpStatusCode.Conflict, missingOne.StatusCode);

        // "Không đổi" = đúng tập quyền hiện có của admin (seeder mặc định có thể cấp thêm quyền ngoài IdentityAccess).
        var currentCodes = admin.GetProperty("permissionCodes").EnumerateArray().Select(p => p.GetString()!).ToArray();
        var unchanged = await PutAsync($"/api/v1/roles/{adminId}/permissions", new { permissionCodes = currentCodes }, version);
        Assert.Equal(HttpStatusCode.OK, unchanged.StatusCode);
        Assert.Equal(version, (await JsonAsync(unchanged)).GetProperty("rowVersion").GetUInt32());
        Assert.Equal(currentCodes.Length, (await GetRoleAsync(adminId)).GetProperty("permissionCodes").GetArrayLength());
        Assert.Equal(before, await TestData.QueryAsync(_factory, db => db.CacheInvalidations.CountAsync()));
    }

    [Fact]
    public async Task SystemRole_NonAdmin_CanBeEditedLikeAnyRole()
    {
        var doctor = await GetRoleByCodeAsync(SystemRoles.Doctor);
        var id = doctor.GetProperty("id").GetGuid();
        var originalName = doctor.GetProperty("name").GetString()!;
        var version = doctor.GetProperty("rowVersion").GetUInt32();

        var renamed = await PutAsync($"/api/v1/roles/{id}", new { name = "Bác sĩ (đã đổi)" }, version);
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        var dto = await JsonAsync(renamed);
        Assert.Equal(SystemRoles.Doctor, dto.GetProperty("code").GetString());
        Assert.True(dto.GetProperty("isSystem").GetBoolean());

        var added = await PutAsync($"/api/v1/roles/{id}/permissions", new { permissionCodes = new[] { Permissions.Catalog.Read } },
            dto.GetProperty("rowVersion").GetUInt32());
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);

        // Trả lại như cũ để không ảnh hưởng test khác dùng chung dữ liệu seed.
        var restoredPermissions = await PutAsync($"/api/v1/roles/{id}/permissions", new { permissionCodes = doctor.GetProperty("permissionCodes").EnumerateArray().Select(p => p.GetString()).ToArray() },
            (await JsonAsync(added)).GetProperty("rowVersion").GetUInt32());
        var restored = await PutAsync($"/api/v1/roles/{id}", new { name = originalName }, (await JsonAsync(restoredPermissions)).GetProperty("rowVersion").GetUInt32());
        Assert.Equal(HttpStatusCode.OK, restored.StatusCode);
    }

    [Fact]
    public async Task Mutations_WriteAuditRecordsAndDiffWithoutRowVersion()
    {
        var (id, version) = await NewRoleAsync("audited");
        (await PutAsync($"/api/v1/roles/{id}/permissions", new { permissionCodes = new[] { Permissions.Catalog.Read } }, version)).EnsureSuccessStatusCode();

        var actions = await TestData.QueryAsync(_factory, db => db.AuditRecords
            .Where(r => r.ResourceType == "Role" && r.ResourceId == id.ToString()).Select(r => r.Action).ToListAsync());
        Assert.Contains("roles.create", actions);
        Assert.Contains("roles.set_permissions", actions);

        var changes = await TestData.QueryAsync(_factory, db => db.AuditLogs
            .Where(l => l.EntityName == "Role" && l.EntityId == id.ToString()).Select(l => l.Changes).ToListAsync());
        Assert.NotEmpty(changes);
        Assert.All(changes, c => Assert.DoesNotContain("RowVersion", c));
        // Lần sửa chỉ đổi bảng con: Name bị đánh dấu modified nhưng không đổi giá trị ⇒ không tạo diff Role rỗng.
        Assert.Single(changes);
    }

    /// SetRolePermissions phải khoá admin-safety (chung với SetUserRoles) rồi mới đọc holder, nếu không gán role
    /// chạy song song có thể lọt khỏi vòng invalidate và member giữ quyền đã gỡ vô thời hạn.
    [Fact]
    public async Task SetRolePermissions_ConcurrentWithRoleAssignment_NeverLeavesStalePermission()
    {
        for (var i = 0; i < 8; i++)
        {
            var (roleId, version) = await NewRoleAsync($"catalog-viewer-{i}", Permissions.Catalog.Read);
            var email = TestData.NewEmail();
            var memberId = await TestData.CreateUserAsync(_factory, email);
            var member = new AuthTestClient(_factory.CreateHttpsClient());
            (await member.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

            var userVersion = (await JsonAsync(await _admin.GetAsync($"/api/v1/users/{memberId}"))).GetProperty("rowVersion").GetUInt32();
            var responses = await Task.WhenAll(
                PutAsync($"/api/v1/users/{memberId}/roles", new { roleIds = new[] { roleId } }, userVersion),
                PutAsync($"/api/v1/roles/{roleId}/permissions", new { permissionCodes = Array.Empty<string>() }, version));

            Assert.All(responses, r => Assert.True(r.IsSuccessStatusCode));
            Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/permissions")).StatusCode);
        }
    }
}

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
public class FacilitiesAdminTests : IAsyncLifetime
{
    private const string Base = "/api/v1/facilities";
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public FacilitiesAdminTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();

    private static string Code() => "T" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();

    private async Task<JsonElement> PostOkAsync(string path, object body)
    {
        var response = await _admin.SendAsync(HttpMethod.Post, path, body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await JsonAsync(response);
    }

    private Task<HttpResponseMessage> PutAsync(string path, object body, string? ifMatch)
        => _admin.SendAsync(HttpMethod.Put, path, body,
            extraHeaders: ifMatch is null ? null : new Dictionary<string, string> { ["If-Match"] = ifMatch });

    private static async Task<string> ErrorCodeAsync(HttpResponseMessage r) => (await JsonAsync(r)).GetProperty("code").GetString()!;

    [Fact]
    public async Task CreateBranchDepartmentRoom_AppearInTree()
    {
        var branch = await PostOkAsync($"{Base}/branches", new { code = Code(), name = "Cơ sở A" });
        var branchId = branch.GetProperty("id").GetGuid();
        var dept = await PostOkAsync($"{Base}/departments", new { branchId, code = "KB", name = "Khám bệnh", kind = "clinical" });
        Assert.Equal("clinical", dept.GetProperty("kind").GetString());
        var deptId = dept.GetProperty("id").GetGuid();
        await PostOkAsync($"{Base}/rooms", new { departmentId = deptId, code = "P2", name = "Phòng 2" });
        await PostOkAsync($"{Base}/rooms", new { departmentId = deptId, code = "P1", name = "Phòng 1" });

        var tree = await JsonAsync(await _admin.GetAsync(Base));
        var node = tree.EnumerateArray().Single(b => b.GetProperty("id").GetGuid() == branchId);
        var d = node.GetProperty("departments").EnumerateArray().Single();
        Assert.Equal("KB", d.GetProperty("code").GetString());
        Assert.Equal(["P1", "P2"], d.GetProperty("rooms").EnumerateArray().Select(r => r.GetProperty("code").GetString()).ToArray());
    }

    [Fact]
    public async Task DuplicateDepartmentCode_Returns409_BadCodeReturns400()
    {
        var branchId = (await PostOkAsync($"{Base}/branches", new { code = Code(), name = "CS" })).GetProperty("id").GetGuid();
        await PostOkAsync($"{Base}/departments", new { branchId, code = "DUP", name = "K1", kind = "clinical" });

        var dup = await _admin.SendAsync(HttpMethod.Post, $"{Base}/departments", new { branchId, code = "DUP", name = "K2", kind = "pharmacy" });
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("facility_code_taken", await ErrorCodeAsync(dup));

        var bad = await _admin.SendAsync(HttpMethod.Post, $"{Base}/departments", new { branchId, code = "bad code", name = "K3", kind = "clinical" });
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    }

    [Fact]
    public async Task UpdateBranch_EnforcesIfMatch()
    {
        var branch = await PostOkAsync($"{Base}/branches", new { code = Code(), name = "CS" });
        var id = branch.GetProperty("id").GetGuid();
        var version = branch.GetProperty("rowVersion").GetUInt32();
        var body = new { name = "CS đổi tên", isActive = true };

        var missing = await PutAsync($"{Base}/branches/{id}", body, null);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal("invalid_if_match", await ErrorCodeAsync(missing));

        var ok = await PutAsync($"{Base}/branches/{id}", body, $"\"{version}\"");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var updated = await JsonAsync(ok);
        Assert.Equal("CS đổi tên", updated.GetProperty("name").GetString());
        Assert.NotEqual(version, updated.GetProperty("rowVersion").GetUInt32());

        var stale = await PutAsync($"{Base}/branches/{id}", body, $"\"{version}\"");
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
        Assert.Equal("facility_version_conflict", await ErrorCodeAsync(stale));
    }

    [Fact]
    public async Task DeactivateDepartmentWithActiveRoom_Returns409_AndCreateRoomUnderInactiveReturns409()
    {
        var branchId = (await PostOkAsync($"{Base}/branches", new { code = Code(), name = "CS" })).GetProperty("id").GetGuid();
        var dept = await PostOkAsync($"{Base}/departments", new { branchId, code = "K", name = "K", kind = "laboratory" });
        var deptId = dept.GetProperty("id").GetGuid();
        var room = await PostOkAsync($"{Base}/rooms", new { departmentId = deptId, code = "R", name = "R" });

        var blocked = await PutAsync($"{Base}/departments/{deptId}", new { name = "K", isActive = false }, dept.GetProperty("rowVersion").GetUInt32().ToString().Quote());
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal("facility_has_active_children", await ErrorCodeAsync(blocked));

        var roomOff = await PutAsync($"{Base}/rooms/{room.GetProperty("id").GetGuid()}", new { name = "R", isActive = false }, room.GetProperty("rowVersion").GetUInt32().ToString().Quote());
        Assert.Equal(HttpStatusCode.OK, roomOff.StatusCode);
        var deptOff = await PutAsync($"{Base}/departments/{deptId}", new { name = "K", isActive = false }, dept.GetProperty("rowVersion").GetUInt32().ToString().Quote());
        Assert.Equal(HttpStatusCode.OK, deptOff.StatusCode);

        var child = await _admin.SendAsync(HttpMethod.Post, $"{Base}/rooms", new { departmentId = deptId, code = "R2", name = "R2" });
        Assert.Equal(HttpStatusCode.Conflict, child.StatusCode);
        Assert.Equal("parent_facility_inactive", await ErrorCodeAsync(child));
    }

    [Fact]
    public async Task Permissions_DoctorReadsOnly_NoRoleForbidden_AnonymousUnauthorized()
    {
        var doctorEmail = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, doctorEmail, roleCodes: SystemRoles.Doctor);
        var doctor = new AuthTestClient(_factory.CreateHttpsClient());
        (await doctor.LoginAsync(doctorEmail, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await doctor.GetAsync(Base)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await doctor.SendAsync(HttpMethod.Post, $"{Base}/branches", new { code = Code(), name = "x" })).StatusCode);

        var bareEmail = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, bareEmail);
        var bare = new AuthTestClient(_factory.CreateHttpsClient());
        (await bare.LoginAsync(bareEmail, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await bare.GetAsync(Base)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await _factory.CreateHttpsClient().GetAsync(Base)).StatusCode);
    }

    [Fact]
    public async Task Mutations_WriteAuditRecords()
    {
        var branch = await PostOkAsync($"{Base}/branches", new { code = Code(), name = "CS" });
        var id = branch.GetProperty("id").GetGuid();
        (await PutAsync($"{Base}/branches/{id}", new { name = "CS2", isActive = true }, branch.GetProperty("rowVersion").GetUInt32().ToString().Quote())).EnsureSuccessStatusCode();

        var actions = await TestData.QueryAsync(_factory, db => db.AuditRecords
            .Where(a => a.ResourceType == "Branch" && a.ResourceId == id.ToString()).Select(a => a.Action).ToListAsync());
        Assert.Contains("facilities.create", actions);
        Assert.Contains("facilities.update", actions);
    }
}

internal static class FacilitiesTestStringExtensions
{
    public static string Quote(this string value) => $"\"{value}\"";
}

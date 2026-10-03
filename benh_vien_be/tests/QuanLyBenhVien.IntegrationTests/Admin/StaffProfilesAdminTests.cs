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
public class StaffProfilesAdminTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public StaffProfilesAdminTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => await r.Content.ReadFromJsonAsync<JsonElement>();
    private static async Task<string> ErrorCodeAsync(HttpResponseMessage r) => (await JsonAsync(r)).GetProperty("code").GetString()!;
    private static string Code() => "S" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant();
    private static string Url(Guid userId) => $"/api/v1/users/{userId}/staff-profile";

    private Task<HttpResponseMessage> PutAsync(string path, object body, uint? ifMatch, AuthTestClient? client = null)
        => (client ?? _admin).SendAsync(HttpMethod.Put, path, body,
            extraHeaders: ifMatch is null ? null : new Dictionary<string, string> { ["If-Match"] = $"\"{ifMatch}\"" });

    private async Task<Guid> NewUserAsync(params string[] roles)
        => await TestData.CreateUserAsync(_factory, TestData.NewEmail(), roleCodes: roles);

    private async Task<(Guid BranchId, Guid DeptId)> NewDepartmentAsync(Guid? branchId = null, bool deactivate = false)
    {
        var b = branchId ?? (await Created(await _admin.SendAsync(HttpMethod.Post, "/api/v1/facilities/branches", new { code = Code(), name = "CS" }))).GetProperty("id").GetGuid();
        var d = await Created(await _admin.SendAsync(HttpMethod.Post, "/api/v1/facilities/departments", new { branchId = b, code = Code(), name = "Khoa", kind = "clinical" }));
        var id = d.GetProperty("id").GetGuid();
        if (deactivate)
            (await PutAsync($"/api/v1/facilities/departments/{id}", new { name = "Khoa", isActive = false }, d.GetProperty("rowVersion").GetUInt32())).EnsureSuccessStatusCode();
        return (b, id);
    }

    private static async Task<JsonElement> Created(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return await JsonAsync(r);
    }

    [Fact]
    public async Task Create_ThenCreateAgain_Returns409()
    {
        var userId = await NewUserAsync();
        var notFound = await _admin.GetAsync(Url(userId));
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal("staff_profile_not_found", await ErrorCodeAsync(notFound));

        var created = await PutAsync(Url(userId), new { staffCode = Code(), isActive = true }, null);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var again = await PutAsync(Url(userId), new { staffCode = Code(), isActive = true }, null);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("staff_profile_exists", await ErrorCodeAsync(again));

        var missingUser = await PutAsync(Url(Guid.NewGuid()), new { staffCode = Code(), isActive = true }, null);
        Assert.Equal("user_not_found", await ErrorCodeAsync(missingUser));
    }

    [Fact]
    public async Task Update_EnforcesVersion_UniqueCode_AndValidation()
    {
        var u1 = await NewUserAsync();
        var u2 = await NewUserAsync();
        var u2Code = Code();
        var p1 = await Created(await PutAsync(Url(u1), new { staffCode = Code(), isActive = true }, null));
        await Created(await PutAsync(Url(u2), new { staffCode = u2Code, isActive = true }, null));
        var v1 = p1.GetProperty("rowVersion").GetUInt32();

        var ok = await PutAsync(Url(u1), new { staffCode = Code(), isActive = false }, v1);
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await JsonAsync(ok);
        Assert.False(body.GetProperty("isActive").GetBoolean());
        var v2 = body.GetProperty("rowVersion").GetUInt32();
        Assert.NotEqual(v1, v2);

        var stale = await PutAsync(Url(u1), new { staffCode = Code(), isActive = true }, v1);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);

        var dup = await PutAsync(Url(u1), new { staffCode = u2Code, isActive = true }, v2);
        Assert.Equal(HttpStatusCode.Conflict, dup.StatusCode);
        Assert.Equal("staff_code_taken", await ErrorCodeAsync(dup));

        var bad = await PutAsync(Url(u1), new { staffCode = "bad code", isActive = true }, v2);
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var missing = await PutAsync(Url(await NewUserAsync()), new { staffCode = Code(), isActive = true }, 1);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("staff_profile_not_found", await ErrorCodeAsync(missing));
    }

    [Fact]
    public async Task SetWorkScopes_UsesBranchFromDatabase_RejectsInvalidDepartments()
    {
        var userId = await NewUserAsync();
        var profile = await Created(await PutAsync(Url(userId), new { staffCode = Code(), isActive = true }, null));
        var (branchId, d1) = await NewDepartmentAsync();
        var (_, d2) = await NewDepartmentAsync(branchId);
        var (_, inactive) = await NewDepartmentAsync(branchId, deactivate: true);

        var ok = await PutAsync($"{Url(userId)}/work-scopes", new { departmentIds = new[] { d1, d2 } }, profile.GetProperty("rowVersion").GetUInt32());
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var body = await JsonAsync(ok);
        var scopes = body.GetProperty("workScopes").EnumerateArray().ToList();
        Assert.Equal(2, scopes.Count);
        // BranchId phải lấy từ khoa trong DB, không phải từ client.
        Assert.All(scopes, s => Assert.Equal(branchId, s.GetProperty("branchId").GetGuid()));
        Assert.Equal(new[] { d1, d2 }.Order(), scopes.Select(s => s.GetProperty("departmentId").GetGuid()).Order());
        var version = body.GetProperty("rowVersion").GetUInt32();
        Assert.NotEqual(profile.GetProperty("rowVersion").GetUInt32(), version);

        foreach (var bad in new[] { Guid.NewGuid(), inactive })
        {
            var resp = await PutAsync($"{Url(userId)}/work-scopes", new { departmentIds = new[] { d1, bad } }, version);
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
            var problem = await JsonAsync(resp);
            Assert.Equal("invalid_departments", problem.GetProperty("code").GetString());
            var listed = problem.GetProperty("errors").GetProperty("departmentIds").EnumerateArray().Select(e => e.GetString()!).ToList();
            Assert.Contains(listed, e => e.Contains(bad.ToString()));
        }

        var after = await JsonAsync(await _admin.GetAsync(Url(userId)));
        Assert.Equal(2, after.GetProperty("workScopes").GetArrayLength());
        Assert.Equal(version, after.GetProperty("rowVersion").GetUInt32());

        var noHeader = await PutAsync($"{Url(userId)}/work-scopes", new { departmentIds = new[] { d1 } }, null);
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);
        Assert.Equal("invalid_if_match", await ErrorCodeAsync(noHeader));

        var stale = await PutAsync($"{Url(userId)}/work-scopes", new { departmentIds = new[] { d1 } }, version + 100);
        Assert.Equal(HttpStatusCode.PreconditionFailed, stale.StatusCode);
    }

    [Fact]
    public async Task SetWorkScopes_ConcurrentSameVersion_OneWins_OtherGets412()
    {
        var userId = await NewUserAsync();
        var profile = await Created(await PutAsync(Url(userId), new { staffCode = Code(), isActive = true }, null));
        var version = profile.GetProperty("rowVersion").GetUInt32();
        var (_, d1) = await NewDepartmentAsync();
        var (_, d2) = await NewDepartmentAsync();

        var results = await Task.WhenAll(
            PutAsync($"{Url(userId)}/work-scopes", new { departmentIds = new[] { d1 } }, version),
            PutAsync($"{Url(userId)}/work-scopes", new { departmentIds = new[] { d2 } }, version));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.PreconditionFailed));
    }

    [Fact]
    public async Task Permissions_ClinicalManagerReadsOnly_DoctorForbidden()
    {
        var target = await NewUserAsync();
        await Created(await PutAsync(Url(target), new { staffCode = Code(), isActive = true }, null));

        var mgrEmail = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, mgrEmail, roleCodes: SystemRoles.ClinicalManager);
        var mgr = new AuthTestClient(_factory.CreateHttpsClient());
        (await mgr.LoginAsync(mgrEmail, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await mgr.GetAsync(Url(target))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(Url(target), new { staffCode = Code(), isActive = true }, 1, mgr)).StatusCode);

        var docEmail = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, docEmail, roleCodes: SystemRoles.Doctor);
        var doc = new AuthTestClient(_factory.CreateHttpsClient());
        (await doc.LoginAsync(docEmail, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await doc.GetAsync(Url(target))).StatusCode);
    }

    [Fact]
    public async Task Mutations_WriteAuditRecords()
    {
        var userId = await NewUserAsync();
        var p = await Created(await PutAsync(Url(userId), new { staffCode = Code(), isActive = true }, null));
        var u = await JsonAsync(await PutAsync(Url(userId), new { staffCode = Code(), isActive = true }, p.GetProperty("rowVersion").GetUInt32()));
        var (_, d) = await NewDepartmentAsync();
        (await PutAsync($"{Url(userId)}/work-scopes", new { departmentIds = new[] { d } }, u.GetProperty("rowVersion").GetUInt32())).EnsureSuccessStatusCode();

        var profileId = p.GetProperty("id").GetGuid().ToString();
        var actions = await TestData.QueryAsync(_factory, db => db.AuditRecords
            .Where(a => a.ResourceType == "StaffProfile" && a.ResourceId == profileId).Select(a => a.Action).ToListAsync());
        Assert.Contains("staff_profiles.create", actions);
        Assert.Contains("staff_profiles.update", actions);
        Assert.Contains("staff_profiles.set_work_scopes", actions);
    }
}

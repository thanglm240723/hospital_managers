using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class UsersAdminTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public UsersAdminTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private Task<Guid> RoleIdAsync(string code)
        => TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync());

    private async Task<HttpResponseMessage> CreateAsync(string email, params Guid[] roleIds)
        => await _admin.SendAsync(HttpMethod.Post, "/api/v1/users", new { email, fullName = "Nguyễn Văn B", roleIds });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Create_Returns201WithInitialPasswordOnce_NoStore_DbOnlyHash_LoginMustChange()
    {
        var email = TestData.NewEmail("Doctor");
        var doctorRoleId = await RoleIdAsync(SystemRoles.Doctor);

        var created = await CreateAsync("  " + email.ToUpperInvariant() + " ", doctorRoleId);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var body = await JsonAsync(created);
        var password = body.GetProperty("initialPassword").GetString()!;
        var user = body.GetProperty("user");
        var id = user.GetProperty("id").GetGuid();
        Assert.Equal(email.ToLowerInvariant(), user.GetProperty("email").GetString());
        Assert.True(user.GetProperty("mustChangePassword").GetBoolean());
        Assert.True(user.GetProperty("isActive").GetBoolean());
        Assert.Equal(SystemRoles.Doctor, user.GetProperty("roles")[0].GetProperty("code").GetString());
        Assert.InRange(password.Length, PasswordPolicy.MinLength, PasswordPolicy.MaxLength);
        Assert.False(PasswordPolicy.ContainsEmailLocalPart(password, email));

        var hash = await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Id == id).Select(u => u.PasswordHash).SingleAsync());
        Assert.NotEqual(password, hash);
        Assert.DoesNotContain(password, hash);
        await using (var scope = _factory.Services.CreateAsyncScope())
            Assert.True(scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Verify(password, hash));

        var detail = await _admin.GetAsync($"/api/v1/users/{id}");
        Assert.DoesNotContain(password, await detail.Content.ReadAsStringAsync());
        Assert.DoesNotContain("password\"", (await detail.Content.ReadAsStringAsync()).Replace("mustChangePassword\"", ""), StringComparison.OrdinalIgnoreCase);

        var auditRows = await TestData.QueryAsync(_factory, db => db.AuditRecords.Select(a => new { a.Metadata, a.Reason }).ToListAsync());
        var auditTexts = auditRows.Select(a => a.Metadata + "|" + a.Reason);
        var logTexts = await TestData.QueryAsync(_factory, db => db.AuditLogs.Select(a => a.Changes).ToListAsync());
        Assert.DoesNotContain(auditTexts.Concat(logTexts), t => t.Contains(password, StringComparison.Ordinal));
        Assert.True(await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.AnyAsync(a => a.Action == AuditActions.UserCreate && a.ResourceId == id.ToString())));

        var login = await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, password);
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.True((await JsonAsync(login)).GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Create_ClientCannotSetIsActiveOrSecurityFields()
    {
        var email = TestData.NewEmail();
        var created = await _admin.SendAsync(HttpMethod.Post, "/api/v1/users",
            new { email, fullName = "A", roleIds = Array.Empty<Guid>(), isActive = false, securityVersion = 99, mustChangePassword = false });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("user").GetProperty("id").GetGuid();
        var (isActive, sv, mcp) = await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Id == id)
            .Select(u => new ValueTuple<bool, int, bool>(u.IsActive, u.SecurityVersion, u.MustChangePassword)).SingleAsync());
        Assert.True(isActive);
        Assert.Equal(1, sv);
        Assert.True(mcp);
    }

    [Fact]
    public async Task Create_DuplicateEmail_Returns409EmailTaken()
    {
        var email = TestData.NewEmail();
        Assert.Equal(HttpStatusCode.Created, (await CreateAsync(email)).StatusCode);

        var again = await CreateAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", (await JsonAsync(again)).GetProperty("code").GetString());
    }

    /// Hai request đồng thời cùng email: cả hai có thể qua bước kiểm tra trước, unique index IX_Users_Email
    /// trên PostgreSQL là điểm quyết định ⇒ đúng một 201, cái còn lại 409 email_taken (không 500).
    [Fact]
    public async Task Create_SameEmailConcurrently_ExactlyOneCreatedOther409EmailTaken()
    {
        for (var i = 0; i < 5; i++)
        {
            var email = TestData.NewEmail("race");
            var responses = await Task.WhenAll(CreateAsync(email), CreateAsync(email), CreateAsync(email.ToUpperInvariant()));

            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
            foreach (var conflict in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
                Assert.Equal("email_taken", (await JsonAsync(conflict)).GetProperty("code").GetString());
            }
        }
    }

    [Fact]
    public async Task Create_InvalidInput_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(TestData.NewEmail(), Guid.NewGuid())).StatusCode);
        var doctor = await RoleIdAsync(SystemRoles.Doctor);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(TestData.NewEmail(), doctor, doctor)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync("not-an-email")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Post, "/api/v1/users",
            new { email = TestData.NewEmail(), fullName = " ", roleIds = Array.Empty<Guid>() })).StatusCode);
    }

    [Fact]
    public async Task Create_WithoutCsrf_IsRejected()
    {
        var response = await _admin.SendAsync(HttpMethod.Post, "/api/v1/users",
            new { email = TestData.NewEmail(), fullName = "A", roleIds = Array.Empty<Guid>() }, csrf: false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_KillsSessionsBlocksLoginAndIsAudited_ActivateRestoresWithoutReviving()
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email);
        await TestData.GrantAsync(_factory, userId, Permissions.Catalog.Read);
        var user = new AuthTestClient(_factory.CreateHttpsClient());
        (await user.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/v1/permissions")).StatusCode);   // cache quyền ấm
        var svBefore = await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityVersion).SingleAsync());

        var response = await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/deactivate");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await JsonAsync(response)).GetProperty("isActive").GetBoolean());
        // Request kế tiếp với access token còn hạn bị từ chối ngay (cache quyền/phiên đã xoá, user đã khoá).
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.GetAsync("/api/v1/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, TestData.DefaultPassword)).StatusCode);
        Assert.Equal(svBefore + 1, await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Id == userId).Select(u => u.SecurityVersion).SingleAsync()));
        Assert.False(await TestData.QueryAsync(_factory, db => db.SessionFamilies.AnyAsync(f => f.UserId == userId && f.Status == SessionStatus.Active)));
        Assert.True(await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.AnyAsync(a => a.Action == AuditActions.UserDeactivate && a.ResourceId == userId.ToString())));

        // Đặt trạng thái đích: gọi lại vẫn 200.
        Assert.Equal(HttpStatusCode.OK, (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/deactivate")).StatusCode);

        var activated = await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/activate");
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.True((await JsonAsync(activated)).GetProperty("isActive").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.RefreshAsync()).StatusCode);   // family cũ không hồi sinh
        Assert.Equal(HttpStatusCode.OK, (await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, TestData.DefaultPassword)).StatusCode);
        Assert.True(await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.AnyAsync(a => a.Action == AuditActions.UserActivate && a.ResourceId == userId.ToString())));
    }

    [Fact]
    public async Task ActivateOrDeactivateUnknownUser_Returns404()
    {
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{Guid.NewGuid()}/deactivate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{Guid.NewGuid()}/activate")).StatusCode);
    }

    [Fact]
    public async Task Deactivate_Self_Returns409AndDoesNotMutate()
    {
        var me = await JsonAsync(await _admin.GetAsync("/api/v1/auth/me"));
        var id = me.GetProperty("id").GetGuid();

        var response = await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{id}/deactivate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("self_action_forbidden", (await JsonAsync(response)).GetProperty("code").GetString());
        Assert.True(await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Id == id).Select(u => u.IsActive).SingleAsync()));
    }

    [Fact]
    public async Task Deactivate_LastActiveAdmin_Returns409()
    {
        var operatorEmail = TestData.NewEmail("operator");
        var operatorId = await TestData.CreateUserAsync(_factory, operatorEmail);
        await TestData.GrantAsync(_factory, operatorId, Permissions.Users.Activate);
        var operatorClient = new AuthTestClient(_factory.CreateHttpsClient());
        (await operatorClient.LoginAsync(operatorEmail, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        var adminId = await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Email == _factory.AdminEmail).Select(u => u.Id).SingleAsync());

        var response = await operatorClient.SendAsync(HttpMethod.Post, $"/api/v1/users/{adminId}/deactivate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last_admin", (await JsonAsync(response)).GetProperty("code").GetString());
        Assert.True(await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Id == adminId).Select(u => u.IsActive).SingleAsync()));
    }
}

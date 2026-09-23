using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class UsersAdminTests : IAsyncLifetime
{
    private const string TempPassword = "Temp-Password-777";
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

    private async Task<HttpResponseMessage> CreateAsync(string email, string password = TempPassword, params Guid[] roleIds)
        => await _admin.SendAsync(HttpMethod.Post, "/api/v1/users",
            new { email, fullName = "Nguyễn Văn B", temporaryPassword = password, roleIds });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Create_ThenSearchAndGetDetail()
    {
        var email = TestData.NewEmail("doctor");
        var created = await CreateAsync(email, TempPassword, await RoleIdAsync(SystemRoles.Doctor));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("id").GetString();

        var page = await JsonAsync(await _admin.GetAsync($"/api/v1/users?searchTerm={Uri.EscapeDataString(email)}"));
        var detail = await JsonAsync(await _admin.GetAsync($"/api/v1/users/{id}"));

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal("doctor", page.GetProperty("items")[0].GetProperty("roleCodes")[0].GetString());
        Assert.True(detail.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal("doctor", detail.GetProperty("roles")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreatedUser_LogsInWithTemporaryPasswordAndMustChangeIt()
    {
        var email = TestData.NewEmail();
        (await CreateAsync(email)).EnsureSuccessStatusCode();

        var user = new AuthTestClient(_factory.CreateHttpsClient());
        var login = await user.LoginAsync(email, TempPassword);

        Assert.True((await JsonAsync(login)).GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Create_DuplicateEmail_Returns409()
    {
        var email = TestData.NewEmail();
        (await CreateAsync(email)).EnsureSuccessStatusCode();

        var again = await CreateAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", (await JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_UnknownRoleOrWeakPassword_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(TestData.NewEmail(), TempPassword, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(TestData.NewEmail(), "short")).StatusCode);
    }

    [Fact]
    public async Task List_PageSizeOver100_Returns400()
        => Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/users?pageSize=101")).StatusCode);

    [Fact]
    public async Task GetUnknownUser_Returns404()
        => Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/v1/users/{Guid.NewGuid()}")).StatusCode);

    [Fact]
    public async Task Deactivate_KillsSessionsBlocksLoginAndIsAudited_ActivateRestores()
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email);
        var user = new AuthTestClient(_factory.CreateHttpsClient());
        (await user.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        var response = await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/deactivate");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, TestData.DefaultPassword)).StatusCode);
        Assert.True(await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.AnyAsync(a => a.Action == AuditActions.UserDeactivate && a.ResourceId == userId.ToString())));

        (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/activate")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, TestData.DefaultPassword)).StatusCode);
    }

    [Fact]
    public async Task Deactivate_Self_Returns409()
    {
        var me = await JsonAsync(await _admin.GetAsync("/api/v1/auth/me"));

        var response = await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{me.GetProperty("id").GetString()}/deactivate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("self_action_forbidden", (await JsonAsync(response)).GetProperty("code").GetString());
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
    }
}

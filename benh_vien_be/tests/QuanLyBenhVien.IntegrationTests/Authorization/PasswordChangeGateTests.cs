using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Carter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Authorization;

/// Route chỉ có trong test host để chứng minh gate chặn route bảo vệ bất kỳ (không thêm route giả vào production).
public sealed class GateProbeModule : ICarterModule
{
    public const string Path = "/api/v1/test-only/gate-probe";
    public static int Hits;

    public void AddRoutes(IEndpointRouteBuilder app)
        => app.MapGet(Path, () =>
        {
            Interlocked.Increment(ref Hits);
            return Results.Ok();
        }).RequireAuthorization();
}

/// Permission service lỗi (DB sập) — gate không được cho request đi tiếp.
public sealed class ThrowingPermissionService : IPermissionService
{
    public Task<UserAccessDto?> GetAsync(Guid userId, CancellationToken ct)
        => throw new InvalidOperationException("db down");
}

[Collection(IntegrationCollection.Name)]
public class PasswordChangeGateTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public PasswordChangeGateTests(ContainersFixture containers) => _containers = containers;

    private static void AddProbe(IServiceCollection services) => services.AddSingleton<ICarterModule, GateProbeModule>();

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers, configureServices: AddProbe);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private static async Task<AuthTestClient> LoginAsync(ApiFactory factory, bool mustChangePassword)
    {
        var email = TestData.NewEmail("gate");
        await TestData.CreateUserAsync(factory, email, mustChangePassword: mustChangePassword);
        var client = new AuthTestClient(factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task MustChangePassword_BlocksProtectedRoute_With403PasswordChangeRequired()
    {
        var client = await LoginAsync(_factory, mustChangePassword: true);
        var before = GateProbeModule.Hits;

        var response = await client.GetAsync(GateProbeModule.Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("password_change_required", await CodeAsync(response));
        Assert.Equal(before, GateProbeModule.Hits);
    }

    [Fact]
    public async Task NormalUser_PassesGate()
    {
        var client = await LoginAsync(_factory, mustChangePassword: false);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(GateProbeModule.Path)).StatusCode);
    }

    [Fact]
    public async Task MustChangePassword_AllowsMe_ChangePassword_Logout()
    {
        var client = await LoginAsync(_factory, mustChangePassword: true);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
        var change = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = TestData.DefaultPassword, newPassword = "Brand-New-Pass-99" });
        Assert.Equal(HttpStatusCode.OK, change.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(GateProbeModule.Path)).StatusCode);
    }

    [Fact]
    public async Task MustChangePassword_LogoutAndLogoutAll_AreNotBlockedByGate()
    {
        var client = await LoginAsync(_factory, mustChangePassword: true);

        // logout-all chưa triển khai ở task này: chỉ khẳng định gate không trả password_change_required.
        var logoutAll = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout-all");
        Assert.NotEqual("password_change_required", await CodeOrNullAsync(logoutAll));
        var logout = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout");
        Assert.NotEqual("password_change_required", await CodeOrNullAsync(logout));
    }

    [Fact]
    public async Task AnonymousRoutes_AreNotAffected()
        => Assert.Equal(HttpStatusCode.OK, (await _factory.CreateHttpsClient().GetAsync("/health")).StatusCode);

    [Fact]
    public async Task ReadingFlagFails_RequestDoesNotProceed()
    {
        var client = await LoginAsync(_factory, mustChangePassword: false);
        await using var broken = _factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
            services.Replace(ServiceDescriptor.Scoped<IPermissionService, ThrowingPermissionService>())));
        var brokenClient = client.CloneWith(broken.CreateClient(new() { BaseAddress = new Uri("https://localhost"), HandleCookies = false }));
        var before = GateProbeModule.Hits;

        var response = await brokenClient.GetAsync(GateProbeModule.Path);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(before, GateProbeModule.Hits);
    }

    private static async Task<string?> CodeOrNullAsync(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/problem+json")) return null;
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}

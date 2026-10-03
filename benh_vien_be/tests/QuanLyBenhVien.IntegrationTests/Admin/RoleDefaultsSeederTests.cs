using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Persistence;
using QuanLyBenhVien.Persistence.Seed;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class RoleDefaultsSeederTests : IAsyncLifetime
{
    // Mã đã kích hoạt hiện chỉ thuộc IdentityAccess; dùng ma trận thử để vai trò doctor có cặp chờ áp.
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> TestMatrix =
        new Dictionary<string, IReadOnlyList<string>> { [SystemRoles.Doctor] = [Permissions.Users.Read] };

    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public RoleDefaultsSeederTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task RunApplierAsync(IReadOnlyDictionary<string, IReadOnlyList<string>>? matrix = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var applier = scope.ServiceProvider.GetRequiredService<RoleDefaultsApplier>();
        if (matrix is null) await applier.ApplyAsync(CancellationToken.None);
        else await applier.ApplyAsync(matrix, Permissions.IsDefined, CancellationToken.None);
    }

    private Task<Guid> DoctorIdAsync() => TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Code == SystemRoles.Doctor).Select(r => r.Id).SingleAsync());

    private Task<int> CountAsync() => TestData.QueryAsync(_factory, async db =>
        await db.RolePermissionDefaults.CountAsync() * 1000 + await db.Roles.SelectMany(r => r.GrantedPermissions).CountAsync());

    [Fact]
    public async Task Startup_AppliesActivatedDefaults()
    {
        var expected = DefaultRolePermissions.ByRole
            .SelectMany(kv => kv.Value.Distinct().Where(Permissions.IsDefined).Select(c => (Role: kv.Key, Code: c))).ToHashSet();
        Assert.NotEmpty(expected);
        var history = await TestData.QueryAsync(_factory, async db => (await db.RolePermissionDefaults
            .Join(db.Roles, d => d.RoleId, r => r.Id, (d, r) => new { r.Code, d.PermissionCode }).ToListAsync())
            .Select(x => (x.Code, x.PermissionCode)).ToHashSet());
        var granted = await TestData.QueryAsync(_factory, async db => (await db.Roles.SelectMany(r => r.GrantedPermissions.Select(p => new { r.Code, p.PermissionCode })).ToListAsync())
            .Select(x => (x.Code, x.PermissionCode)).ToHashSet());
        Assert.True(expected.IsSubsetOf(history));
        Assert.True(expected.IsSubsetOf(granted));
    }

    [Fact]
    public async Task Apply_IsIdempotent()
    {
        await RunApplierAsync(TestMatrix);
        var before = await CountAsync();
        await RunApplierAsync(TestMatrix);
        await RunApplierAsync();
        Assert.Equal(before, await CountAsync());
    }

    [Fact]
    public async Task RemovedDefault_IsNotReapplied()
    {
        await RunApplierAsync(TestMatrix);
        var doctorId = await DoctorIdAsync();
        var admin = await _factory.LoginAsAdminAsync();
        var roles = await admin.GetAsync("/api/v1/roles");
        var role = (await roles.Content.ReadFromJsonAsync<JsonElement>()).EnumerateArray().Single(r => r.GetProperty("id").GetGuid() == doctorId);
        Assert.Contains(Permissions.Users.Read, role.GetProperty("permissionCodes").EnumerateArray().Select(e => e.GetString()));

        var put = await admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{doctorId}/permissions", new { permissionCodes = Array.Empty<string>() },
            extraHeaders: new Dictionary<string, string> { ["If-Match"] = $"\"{role.GetProperty("rowVersion").GetUInt32()}\"" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        await RunApplierAsync(TestMatrix);

        Assert.False(await TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Id == doctorId).SelectMany(r => r.GrantedPermissions).AnyAsync()));
    }

    [Fact]
    public async Task NewlyAppliedDefault_IsVisibleOnNextRequest()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email, roleCodes: SystemRoles.Doctor);
        var user = new AuthTestClient(_factory.CreateHttpsClient());
        (await user.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/users")).StatusCode);   // cache quyền ấm: chưa có users.read

        await RunApplierAsync(TestMatrix);

        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task ConcurrentApply_DoesNotDuplicate()
    {
        await Task.WhenAll(RunApplierAsync(TestMatrix), RunApplierAsync(TestMatrix));
        var doctorId = await DoctorIdAsync();
        Assert.Equal(1, await TestData.QueryAsync(_factory, db => db.RolePermissionDefaults
            .CountAsync(d => d.RoleId == doctorId && d.PermissionCode == Permissions.Users.Read)));
        Assert.Equal(1, await TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Id == doctorId).SelectMany(r => r.GrantedPermissions).CountAsync(p => p.PermissionCode == Permissions.Users.Read)));
    }
}

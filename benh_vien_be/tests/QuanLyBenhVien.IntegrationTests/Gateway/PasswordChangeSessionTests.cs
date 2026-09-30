using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Gateway;

/// Nghiệm thu đổi mật khẩu qua Gateway, không phụ thuộc refresh (plan 01 task 5).
/// Gateway là lớp từ chối token cũ: key `session:{fid}` bị xoá sau commit ⇒ Gateway hỏi lại API với sv của token.
[Collection(IntegrationCollection.Name)]
public class PasswordChangeSessionTests
{
    private const string Me = "/api/v1/auth/me";
    private const string NewPassword = "Brand-New-Pass-99";
    private readonly ContainersFixture _containers;

    public PasswordChangeSessionTests(ContainersFixture containers) => _containers = containers;

    private static IDatabase Redis(ApiFactory api) => api.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private static Guid Fid(string token) => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).GetClaim("fid").Value);

    private static async Task<AuthTestClient> LoginAsync(GatewayFactory gateway, string email, string password)
    {
        var client = new AuthTestClient(gateway.CreateHttpsClient());
        (await client.LoginAsync(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> ChangeAsync(AuthTestClient client, string current, string next)
        => client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password", new { currentPassword = current, newPassword = next });

    private static async Task<HttpStatusCode> MeWithAsync(GatewayFactory gateway, string token)
        => (await new AuthTestClient(gateway.CreateHttpsClient()) { AccessToken = token }.GetAsync(Me)).StatusCode;

    [Fact]
    public async Task AfterChange_OldTokensOfAllFamiliesAre401_NewTokenIs200_CurrentFamilyStaysActive()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        await using var gateway = new GatewayFactory(api, _containers.RedisConnectionString);
        var email = TestData.NewEmail("gw-pwd");
        var userId = await TestData.CreateUserAsync(api, email);
        var current = await LoginAsync(gateway, email, TestData.DefaultPassword);
        var other = await LoginAsync(gateway, email, TestData.DefaultPassword);
        var oldCurrent = current.AccessToken!;
        var oldOther = other.AccessToken!;
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, oldCurrent)); // cache session đã có sv cũ

        (await ChangeAsync(current, TestData.DefaultPassword, NewPassword)).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, oldCurrent));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, oldOther));
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, current.AccessToken!));
        // Sau khi token mới nạp lại cache (sv mới), token cũ vẫn bị từ chối từ cache.
        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, oldCurrent));
        var status = await TestData.QueryAsync(api, db => db.SessionFamilies.AsNoTracking()
            .Where(f => f.Id == Fid(current.AccessToken!)).Select(f => f.Status).SingleAsync());
        Assert.Equal(SessionStatus.Active, status);
        Assert.True(await TestData.QueryAsync(api, db => db.Users.AnyAsync(u => u.Id == userId && !u.MustChangePassword)));
    }

    [Fact]
    public async Task AfterChange_RedisMissAndIdentityServiceDown_Is503_NotTreatedAsValid()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        await using var gateway = new GatewayFactory(api, _containers.RedisConnectionString, identityServiceDown: true);
        var email = TestData.NewEmail("gw-pwd");
        await TestData.CreateUserAsync(api, email);
        var client = await LoginAsync(gateway, email, TestData.DefaultPassword); // login ghi cache ⇒ không cần hỏi API
        var oldToken = client.AccessToken!;

        (await ChangeAsync(client, TestData.DefaultPassword, NewPassword)).EnsureSuccessStatusCode();

        Assert.False(await Redis(api).KeyExistsAsync(CacheKeys.Session(Fid(oldToken))));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await MeWithAsync(gateway, oldToken));
        Assert.Equal(HttpStatusCode.ServiceUnavailable, await MeWithAsync(gateway, client.AccessToken!));
    }

    [Fact]
    public async Task FlushFailsAfterCommit_PasswordChanged_RowKept_OldTokenRejectedOnlyAfterWorkerRuns()
    {
        var evictor = new ToggleEvictor();
        await using var api = await ApiFactory.CreateAsync(_containers, configureServices: s =>
        {
            TestServices.RemoveCacheInvalidationWorker(s);
            s.Replace(ServiceDescriptor.Singleton<ICacheKeyEvictor>(evictor));
        });
        await using var gateway = new GatewayFactory(api, _containers.RedisConnectionString);
        var email = TestData.NewEmail("gw-pwd");
        var userId = await TestData.CreateUserAsync(api, email);
        var client = await LoginAsync(gateway, email, TestData.DefaultPassword);
        var oldToken = client.AccessToken!;

        (await ChangeAsync(client, TestData.DefaultPassword, NewPassword)).EnsureSuccessStatusCode();

        var hash = await TestData.QueryAsync(api, db => db.Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync());
        Assert.True(api.Services.GetRequiredService<IPasswordHasher>().Verify(NewPassword, hash));
        var keys = await TestData.QueryAsync(api, db => db.CacheInvalidations.Select(r => r.Key).ToListAsync());
        Assert.Contains(CacheKeys.Session(Fid(oldToken)), keys);
        Assert.Contains(CacheKeys.Permissions(userId), keys);

        // Khoảng chưa đồng bộ đã biết: cache vẫn giữ sv cũ ⇒ Gateway còn nhận token cũ và từ chối token mới
        // cho tới khi worker xoá key. Không phải "thu hồi ngay lập tức" khi eviction thất bại.
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, oldToken));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, client.AccessToken!));

        evictor.Redis = Redis(api);
        using (var scope = api.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>().ProcessPendingAsync(100, CancellationToken.None);

        Assert.Equal(0, await TestData.QueryAsync(api, db => db.CacheInvalidations.CountAsync()));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, oldToken));
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, client.AccessToken!));
    }

    [Fact]
    public async Task SeededAdmin_ChangesTempPassword_OldPasswordRejected_NewPasswordWorks()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        await using var gateway = new GatewayFactory(api, _containers.RedisConnectionString);
        var admin = await LoginAsync(gateway, api.AdminEmail, TestConstants.AdminTempPassword);
        var me = await (await admin.GetAsync(Me)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(me.GetProperty("mustChangePassword").GetBoolean());

        var response = await ChangeAsync(admin, TestConstants.AdminTempPassword, TestConstants.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await new AuthTestClient(gateway.CreateHttpsClient()).LoginAsync(api.AdminEmail, TestConstants.AdminTempPassword)).StatusCode);
        var fresh = await LoginAsync(gateway, api.AdminEmail, TestConstants.AdminPassword);
        me = await (await fresh.GetAsync(Me)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
    }

    private sealed class ToggleEvictor : ICacheKeyEvictor
    {
        /// null ⇒ mô phỏng Redis không tới được; đặt vào ⇒ xoá key thật (evictor thật là internal).
        public volatile IDatabase? Redis;

        public async Task EvictAsync(IReadOnlyCollection<string> keys, CancellationToken ct)
        {
            if (Redis is null)
                throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "unreachable (test)");
            await Redis.KeyDeleteAsync(keys.Select(k => (RedisKey)k).ToArray());
        }
    }
}

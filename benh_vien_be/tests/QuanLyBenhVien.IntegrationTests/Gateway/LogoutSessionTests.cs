using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Gateway;

/// Task 2 (plan 02 logout): sau logout/logout-all qua Gateway, access token cũ của family bị thu hồi bị từ chối
/// (key `session:{fid}` bị xoá sau commit ⇒ Gateway hỏi lại API). Không dùng /refresh làm bằng chứng.
[Collection(IntegrationCollection.Name)]
public class LogoutSessionTests
{
    private const string Me = "/api/v1/auth/me";
    private readonly ContainersFixture _containers;

    public LogoutSessionTests(ContainersFixture containers) => _containers = containers;

    private static Guid Fid(string token) => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(token).GetClaim("fid").Value);

    private static async Task<AuthTestClient> LoginAsync(GatewayFactory gateway, string email)
    {
        var client = new AuthTestClient(gateway.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static async Task<HttpStatusCode> MeWithAsync(GatewayFactory gateway, string token)
        => (await new AuthTestClient(gateway.CreateHttpsClient()) { AccessToken = token }.GetAsync(Me)).StatusCode;

    [Fact]
    public async Task Logout_ViaGateway_OldTokenOfThatFamilyIs401_OtherFamilyStill200()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        await using var gateway = new GatewayFactory(api, _containers.RedisConnectionString);
        var email = TestData.NewEmail("gw-logout");
        await TestData.CreateUserAsync(api, email);
        var a = await LoginAsync(gateway, email);
        var b = await LoginAsync(gateway, email);
        var tokenA = a.AccessToken!;
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, tokenA)); // cache session đã có

        var response = await a.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, tokenA));
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, b.AccessToken!));
        var status = await TestData.QueryAsync(api, db => db.SessionFamilies.AsNoTracking()
            .Where(f => f.Id == Fid(tokenA)).Select(f => f.Status).SingleAsync());
        Assert.Equal(SessionStatus.Revoked, status);
    }

    /// Task 4 (plan 02 logout): logout qua Gateway thực sự xoá cookie (Set-Cookie __Host-rt/__Host-csrf,
    /// vẫn Secure/SameSite=Strict/Path=/) và route công khai (không cần bearer) qua cookie forward.
    [Fact]
    public async Task Logout_ViaGateway_ClearsCookies_AnonymousReachableWithCookieForwarded()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        await using var gateway = new GatewayFactory(api, _containers.RedisConnectionString);
        var email = TestData.NewEmail("gw-logout-cookie");
        await TestData.CreateUserAsync(api, email);
        var client = await LoginAsync(gateway, email);

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies), "Thiếu Set-Cookie qua Gateway.");
        var cookies = setCookies.ToList();
        var rt = cookies.Single(c => c.StartsWith("__Host-rt=")).ToLowerInvariant();
        var csrf = cookies.Single(c => c.StartsWith("__Host-csrf=")).ToLowerInvariant();
        foreach (var cookie in new[] { rt, csrf })
        {
            Assert.Contains("secure", cookie);
            Assert.Contains("samesite=strict", cookie);
            Assert.Contains("path=/", cookie);
            Assert.True(cookie.Contains("expires=") || cookie.Contains("max-age=0"),
                $"Cookie không có dấu hiệu hết hạn qua Gateway: {cookie}");
        }
        Assert.Contains("httponly", rt);
        Assert.DoesNotContain("httponly", csrf);
        Assert.Null(client.RefreshToken);
        Assert.Null(client.CsrfToken);
    }

    [Fact]
    public async Task LogoutAll_ViaGateway_TokensOfAllFamiliesAre401_OtherUserStill200()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        await using var gateway = new GatewayFactory(api, _containers.RedisConnectionString);
        var email = TestData.NewEmail("gw-logout-all");
        await TestData.CreateUserAsync(api, email);
        var otherEmail = TestData.NewEmail("gw-bystander");
        await TestData.CreateUserAsync(api, otherEmail);
        var a = await LoginAsync(gateway, email);
        var b = await LoginAsync(gateway, email);
        var other = await LoginAsync(gateway, otherEmail);
        var tokenA = a.AccessToken!;
        var tokenB = b.AccessToken!;
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, tokenB));

        var response = await a.SendAsync(HttpMethod.Post, "/api/v1/auth/logout-all");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, tokenA));
        Assert.Equal(HttpStatusCode.Unauthorized, await MeWithAsync(gateway, tokenB));
        Assert.Equal(HttpStatusCode.OK, await MeWithAsync(gateway, other.AccessToken!));
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

/// Task 2 (plan 03 refresh): endpoint POST /api/v1/auth/refresh qua HTTP thật — cookie, CSRF theo family, thuộc tính Set-Cookie.
[Collection(IntegrationCollection.Name)]
public class RefreshHttpTests : IAsyncLifetime
{
    private const string Refresh = "/api/v1/auth/refresh";
    private readonly ContainersFixture _containers;
    private ApiFactory _api = default!;

    public RefreshHttpTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _api = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _api.DisposeAsync();

    private async Task<AuthTestClient> LoggedInAsync(string prefix = "refresh-http")
    {
        var email = TestData.NewEmail(prefix);
        await TestData.CreateUserAsync(_api, email);
        var client = new AuthTestClient(_api.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static Guid Fid(AuthTestClient client)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim("fid").Value);

    private Task<SessionFamily> FamilyAsync(Guid id)
        => TestData.QueryAsync(_api, db => db.SessionFamilies.AsNoTracking().SingleAsync(f => f.Id == id));

    private Task<int> TokenCountAsync(Guid familyId)
        => TestData.QueryAsync(_api, db => db.RefreshTokens.AsNoTracking().CountAsync(t => t.FamilyId == familyId));

    private static string Cookie(HttpResponseMessage response, string name)
        => response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith($"{name}="));

    private static void AssertCleared(HttpResponseMessage response, string name, bool httpOnly)
    {
        var cookie = Cookie(response, name).ToLowerInvariant();
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("domain=", cookie);
        Assert.True(cookie.Contains("expires=") || cookie.Contains("max-age=0"), cookie);
        Assert.Equal(httpOnly, cookie.Contains("httponly"));
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task MissingCookie_Returns401_ClearsCookies()
    {
        var client = new AuthTestClient(_api.CreateHttpsClient());

        var response = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(response));
        AssertCleared(response, "__Host-rt", httpOnly: true);
        AssertCleared(response, "__Host-csrf", httpOnly: false);
    }

    [Fact]
    public async Task GarbageCookie_Returns401_ClearsCookies()
    {
        var client = new AuthTestClient(_api.CreateHttpsClient()) { RefreshToken = "rac-khong-ton-tai", CsrfToken = "x" };

        var response = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertCleared(response, "__Host-rt", httpOnly: true);
        AssertCleared(response, "__Host-csrf", httpOnly: false);
    }

    [Fact]
    public async Task ForeignOrigin_RecognizedFamily_Returns403_NoRotation()
    {
        var client = await LoggedInAsync();
        var fid = Fid(client);
        var before = await TokenCountAsync(fid);

        var response = await client.SendAsync(HttpMethod.Post, Refresh, bearer: false, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
        Assert.Equal(before, await TokenCountAsync(fid));
        Assert.Equal(SessionStatus.Active, (await FamilyAsync(fid)).Status);
    }

    [Fact]
    public async Task MissingOrWrongCsrf_RecognizedFamily_Returns403_NoRotation_NoRevoke()
    {
        var client = await LoggedInAsync();
        var fid = Fid(client);
        var before = await TokenCountAsync(fid);

        var missing = await client.SendAsync(HttpMethod.Post, Refresh, csrf: false, bearer: false);
        var token = client.CsrfToken;
        client.CsrfToken = "wrong-token";
        var wrong = await client.SendAsync(HttpMethod.Post, Refresh, bearer: false);
        client.CsrfToken = token;

        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, wrong.StatusCode);
        Assert.Equal(before, await TokenCountAsync(fid));
        Assert.Equal(SessionStatus.Active, (await FamilyAsync(fid)).Status);
        // Cookie vẫn dùng được sau khi bị từ chối vì CSRF.
        Assert.Equal(HttpStatusCode.OK, (await client.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Success_Returns200WithOnlyAccessFields_AndRotatesCookies_WithoutExtendingExpiry()
    {
        var client = await LoggedInAsync();
        var fid = Fid(client);
        var oldRefresh = client.RefreshToken;
        var expiry = (await FamilyAsync(fid)).AbsoluteExpiresAtUtc;

        var response = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(
            new[] { "accessToken", "expiresAtUtc", "mustChangePassword" },
            json.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.NotNull(client.RefreshToken);
        Assert.NotEqual(oldRefresh, client.RefreshToken);
        Assert.DoesNotContain(client.RefreshToken!, body);
        Assert.Equal(fid, Fid(client));

        var rt = Cookie(response, "__Host-rt").ToLowerInvariant();
        Assert.Contains("httponly", rt);
        Assert.Contains("secure", rt);
        Assert.Contains("samesite=strict", rt);
        Assert.Contains("path=/", rt);
        Assert.DoesNotContain("domain=", rt);
        var csrf = Cookie(response, "__Host-csrf").ToLowerInvariant();
        Assert.DoesNotContain("httponly", csrf);
        Assert.Contains("secure", csrf);
        Assert.Contains("samesite=strict", csrf);

        // Hạn tuyệt đối không đổi; Max-Age ≤ thời gian còn lại, không bị reset về 7 ngày.
        Assert.Equal(expiry, (await FamilyAsync(fid)).AbsoluteExpiresAtUtc);
        var maxAge = int.Parse(System.Text.RegularExpressions.Regex.Match(rt, @"max-age=(\d+)").Groups[1].Value);
        Assert.InRange(maxAge, 1, (int)(expiry - DateTimeOffset.UtcNow).TotalSeconds + 1);
    }

    [Fact]
    public async Task MaxAge_ShrinksWithAbsoluteExpiry_NotResetByRefresh()
    {
        var client = await LoggedInAsync();
        var fid = Fid(client);
        await TestData.QueryAsync(_api, async db =>
        {
            await db.SessionFamilies.Where(f => f.Id == fid)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.AbsoluteExpiresAtUtc, DateTimeOffset.UtcNow.AddHours(1)));
            return 0;
        });

        var response = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var maxAge = int.Parse(System.Text.RegularExpressions.Regex.Match(Cookie(response, "__Host-rt").ToLowerInvariant(), @"max-age=(\d+)").Groups[1].Value);
        Assert.InRange(maxAge, 1, 3600);
    }

    [Fact]
    public async Task ReusedToken_WithValidCsrf_Returns401_ClearsCookies_RevokesFamily()
    {
        var client = await LoggedInAsync();
        var fid = Fid(client);
        var stale = client.CloneWith(_api.CreateHttpsClient());
        (await client.RefreshAsync()).EnsureSuccessStatusCode();

        var response = await stale.RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertCleared(response, "__Host-rt", httpOnly: true);
        AssertCleared(response, "__Host-csrf", httpOnly: false);
        Assert.Equal(SessionStatus.Revoked, (await FamilyAsync(fid)).Status);
    }
}

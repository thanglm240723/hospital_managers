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

/// Task 1 (plan 02 logout): filter CSRF theo cookie refresh cho route công khai (logout) — Origin + X-CSRF-Token
/// khi family nhận diện được từ cookie `__Host-rt`; cookie thiếu/không nhận diện được thì không có gì để bảo vệ
/// (204, xoá cookie, không mutate). Thu hồi thật khi cookie nhận diện được + CSRF hợp lệ thuộc Task 2.
[Collection(IntegrationCollection.Name)]
public class CookieCsrfTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public CookieCsrfTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<AuthTestClient> LoggedInAsync(string prefix = "csrf")
    {
        var email = TestData.NewEmail(prefix);
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static Guid FamilyOf(AuthTestClient client)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim("fid").Value);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    private Task<SessionStatus> StatusOfAsync(Guid familyId)
        => TestData.QueryAsync(_factory, db => db.SessionFamilies.Where(f => f.Id == familyId).Select(f => f.Status).SingleAsync());

    private Task<int> AuditCountAsync()
        => TestData.QueryAsync(_factory, db => db.AuditRecords.CountAsync());

    /// Cookie xoá phải cùng Path/Secure/SameSite như lúc tạo (`AuthCookieWriter`), khác giá trị/hết hạn ngay.
    private static void AssertCookieCleared(HttpResponseMessage response, string name, bool httpOnly)
    {
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var setCookies), "Thiếu Set-Cookie để xoá cookie.");
        var cookie = setCookies.Single(c => c.StartsWith($"{name}=")).ToLowerInvariant();
        Assert.Contains("secure", cookie);
        Assert.Contains("samesite=strict", cookie);
        Assert.Contains("path=/", cookie);
        Assert.DoesNotContain("domain=", cookie);
        Assert.True(cookie.Contains("expires=") || cookie.Contains("max-age=0"),
            $"Cookie {name} không có dấu hiệu hết hạn: {cookie}");
        if (httpOnly)
        {
            Assert.Contains("httponly", cookie);
        }
        else
        {
            Assert.DoesNotContain("httponly", cookie);
        }
    }

    private static void AssertCookiesCleared(HttpResponseMessage response)
    {
        AssertCookieCleared(response, "__Host-rt", httpOnly: true);
        AssertCookieCleared(response, "__Host-csrf", httpOnly: false);
    }

    [Fact]
    public async Task Logout_ForeignOrigin_CookieRecognized_Returns403NotRevoked()
    {
        var client = await LoggedInAsync();
        var family = FamilyOf(client);
        var auditsBefore = await AuditCountAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout",
            bearer: false, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
        Assert.NotNull(client.RefreshToken);
        Assert.Equal(SessionStatus.Active, await StatusOfAsync(family));
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }

    [Fact]
    public async Task Logout_MissingCsrfHeader_CookieRecognized_Returns403NotRevoked()
    {
        var client = await LoggedInAsync();
        var family = FamilyOf(client);
        var auditsBefore = await AuditCountAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", csrf: false, bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
        Assert.NotNull(client.RefreshToken);
        Assert.Equal(SessionStatus.Active, await StatusOfAsync(family));
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }

    [Fact]
    public async Task Logout_WrongCsrfToken_CookieRecognized_Returns403NotRevoked()
    {
        var client = await LoggedInAsync();
        var family = FamilyOf(client);
        var auditsBefore = await AuditCountAsync();
        client.CsrfToken = "wrong-token";

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
        Assert.NotNull(client.RefreshToken);
        Assert.Equal(SessionStatus.Active, await StatusOfAsync(family));
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }

    [Fact]
    public async Task Logout_CsrfTokenFromDifferentFamily_Returns403NotRevoked()
    {
        var owner = await LoggedInAsync("owner");
        var other = await LoggedInAsync("other");
        var ownerFamily = FamilyOf(owner);
        var otherFamily = FamilyOf(other);
        var auditsBefore = await AuditCountAsync();
        owner.CsrfToken = other.CsrfToken; // token hợp lệ nhưng thuộc family khác cookie hiện tại

        var response = await owner.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
        Assert.NotNull(owner.RefreshToken);
        Assert.Equal(SessionStatus.Active, await StatusOfAsync(ownerFamily));
        Assert.Equal(SessionStatus.Active, await StatusOfAsync(otherFamily));
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }

    [Fact]
    public async Task Logout_MissingCookie_Returns204AndClearsCookies_NoMutation()
    {
        // Không cookie => không family nào để bảo vệ/thu hồi — khác /refresh (vốn đòi hỏi cookie, trả 401 khi thiếu).
        var bystander = await LoggedInAsync("bystander");
        var bystanderFamily = FamilyOf(bystander);
        var auditsBefore = await AuditCountAsync();

        var response = await new AuthTestClient(_factory.CreateHttpsClient())
            .SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertCookiesCleared(response);
        Assert.Equal(SessionStatus.Active, await StatusOfAsync(bystanderFamily));
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }

    [Fact]
    public async Task Logout_UnrecognizedCookie_Returns204AndClearsCookies_NoMutation()
    {
        var bystander = await LoggedInAsync("bystander");
        var bystanderFamily = FamilyOf(bystander);
        var auditsBefore = await AuditCountAsync();
        var client = new AuthTestClient(_factory.CreateHttpsClient())
        {
            RefreshToken = "unknown-refresh-token-value",
        };

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", csrf: false, bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        AssertCookiesCleared(response);
        Assert.Null(client.RefreshToken);
        Assert.Null(client.CsrfToken);
        Assert.Equal(SessionStatus.Active, await StatusOfAsync(bystanderFamily));
        Assert.Equal(auditsBefore, await AuditCountAsync());
    }
}

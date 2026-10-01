using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

/// Test liên tính năng qua HTTP + PostgreSQL thật: rotate, replay thu hồi family, đua song song,
/// hết hạn 7 ngày, CSRF/Origin của refresh, logout/logout-all so với refresh, mất response refresh.
[Collection(IntegrationCollection.Name)]
public class LogoutRefreshInteropTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private ApiFactory _factory = default!;

    public LogoutRefreshInteropTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
        => _factory = await ApiFactory.CreateAsync(_containers, configureServices: s => s.AddSingleton<TimeProvider>(_time));

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<AuthTestClient> LoggedInAsync()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static Guid FamilyOf(AuthTestClient client)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim("fid").Value);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Refresh_RotatesCookieAndKeepsSameSession()
    {
        var client = await LoggedInAsync();
        var oldRefresh = client.RefreshToken;
        var family = FamilyOf(client);

        var response = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(oldRefresh, client.RefreshToken);
        Assert.Equal(family, FamilyOf(client));
    }

    [Fact]
    public async Task Refresh_ReplayedOldToken_RevokesWholeFamily()
    {
        var client = await LoggedInAsync();
        var stolen = client.CloneWith(_factory.CreateHttpsClient());   // bản sao nguyên cookie jar (token + CSRF cũ)
        (await client.RefreshAsync()).EnsureSuccessStatusCode();

        var replay = await stolen.RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Null(stolen.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Refresh_TwoConcurrentWithSameToken_ExactlyOneSucceeds_FamilyRevoked()
    {
        var first = await LoggedInAsync();
        var second = first.CloneWith(_factory.CreateHttpsClient());

        var responses = await Task.WhenAll(first.RefreshAsync(), second.RefreshAsync());

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        var winner = responses[0].StatusCode == HttpStatusCode.OK ? first : second;
        Assert.Equal(HttpStatusCode.Unauthorized, (await winner.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Logout_ThenRefresh_Returns401()
    {
        var client = await LoggedInAsync();
        var copy = client.CloneWith(_factory.CreateHttpsClient());   // kẻ giữ bản sao cookie cũ

        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await copy.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task LogoutAll_EveryOldFamilyRefresh_Returns401()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var clients = new List<AuthTestClient>();
        for (var i = 0; i < 3; i++)
        {
            var c = new AuthTestClient(_factory.CreateHttpsClient());
            (await c.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
            clients.Add(c);
        }
        var copies = clients.Select(c => c.CloneWith(_factory.CreateHttpsClient())).ToList();

        Assert.Equal(HttpStatusCode.NoContent, (await clients[0].SendAsync(HttpMethod.Post, "/api/v1/auth/logout-all")).StatusCode);

        foreach (var copy in copies)
            Assert.Equal(HttpStatusCode.Unauthorized, (await copy.RefreshAsync()).StatusCode);
    }

    /// UX đã chốt: mất response refresh thì FE không tự retry token cũ; nếu vẫn trình lại token cũ thì strict reuse
    /// thu hồi family và người dùng phải đăng nhập lại (cả token mới mà server đã cấp cũng chết).
    [Fact]
    public async Task LostRefreshResponse_ResubmittingOldToken_RevokesFamily_RequiresRelogin()
    {
        var client = await LoggedInAsync();
        var browser = client.CloneWith(_factory.CreateHttpsClient());   // trạng thái cookie của trình duyệt (không nhận response)
        var serverSide = await client.RefreshAsync();   // server đã rotate, response bị mất
        Assert.Equal(HttpStatusCode.OK, serverSide.StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await browser.RefreshAsync()).StatusCode);   // trình lại token cũ -> reuse
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);   // token mới đã cấp cũng chết
    }

    [Fact]
    public async Task Refresh_MissingCsrfHeader_Returns403()
    {
        var client = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf: false, bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
    }

    [Fact]
    public async Task Refresh_ForeignOrigin_Returns403()
    {
        var client = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", bearer: false, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// M3 (final review): một mục rỗng trong Auth:AllowedOrigins (vd. cấu hình rỗng "") không được khớp
    /// Origin rỗng của một request KHÔNG gửi header Origin — nếu không, request đó lọt qua kiểm tra CSRF.
    [Fact]
    public async Task Refresh_EmptyAllowedOriginsEntry_NoOriginHeaderStillRejected()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["Auth:AllowedOrigins:1"] = "" });
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(factory, email);
        var client = new AuthTestClient(factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", bearer: false, origin: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient()).RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterSevenDays_Returns401EvenIfActive()
    {
        var client = await LoggedInAsync();
        _time.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Refresh_ActiveSession_NoCsrfCookieNoHeader_Returns403()
    {
        var client = await LoggedInAsync();
        client.CsrfToken = null;   // trình duyệt không còn giữ cookie __Host-csrf (vd. bị xoá bởi tiện ích/tràn cookie jar)

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf: false, bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
    }
}

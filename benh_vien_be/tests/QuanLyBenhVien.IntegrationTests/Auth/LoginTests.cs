using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class LoginTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public LoginTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private AuthTestClient NewClient() => new(_factory.CreateHttpsClient());

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task ValidCredentials_ReturnAccessTokenAndHardenedCookies()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = NewClient();

        var response = await client.LoginAsync(email, TestData.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await BodyAsync(response)).GetProperty("mustChangePassword").GetBoolean());

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refresh = cookies.Single(c => c.StartsWith("__Host-rt=")).ToLowerInvariant();
        Assert.Contains("httponly", refresh);
        Assert.Contains("secure", refresh);
        Assert.Contains("samesite=strict", refresh);
        Assert.Contains("path=/", refresh);
        Assert.DoesNotContain("domain=", refresh);
        Assert.Contains("max-age=", refresh);
        Assert.DoesNotContain("httponly", cookies.Single(c => c.StartsWith("__Host-csrf=")).ToLowerInvariant());

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken);
        Assert.Equal(
            new[] { "aud", "exp", "fid", "iat", "iss", "jti", "nbf", "sub", "sv" },
            jwt.Claims.Select(c => c.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WrongPassword_UnknownEmail_InactiveAccount_AllLookIdentical()
    {
        var active = TestData.NewEmail();
        var inactive = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, active);
        await TestData.CreateUserAsync(_factory, inactive, isActive: false);

        var titles = new List<string?>();
        foreach (var (email, password) in new[]
                 {
                     (active, "Wrong-Password-1"),
                     (TestData.NewEmail(), TestData.DefaultPassword),
                     (inactive, TestData.DefaultPassword),
                 })
        {
            var response = await NewClient().LoginAsync(email, password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await BodyAsync(response);
            Assert.Equal("unauthenticated", body.GetProperty("code").GetString());
            titles.Add(body.GetProperty("title").GetString());
        }

        Assert.Equal("Email hoặc mật khẩu không đúng.", Assert.Single(titles.Distinct()));
    }

    [Fact]
    public async Task Failure_IsAuditedWithReasonAndActor()
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email);

        await NewClient().LoginAsync(email, "Wrong-Password-1");

        var record = await TestData.QueryAsync(_factory, db => db.AuditRecords
            .SingleAsync(a => a.Action == AuditActions.Login && a.ActorId == userId));
        Assert.Equal(AuditResult.Failed, record.Result);
        Assert.Equal("InvalidPassword", record.Reason);
    }

    [Fact]
    public async Task FiveFailures_SixthAttemptIsRateLimited()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().LoginAsync(email, "Wrong-Password-1")).StatusCode);

        var response = await NewClient().LoginAsync(email, TestData.DefaultPassword);   // kể cả đúng mật khẩu

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(int.Parse(response.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Equal("rate_limited", (await BodyAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task EmptyFields_Return400WithFieldErrors()
    {
        var response = await NewClient().LoginAsync("", "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("email", out _));
        Assert.True(body.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Success_CachesSessionForGatewayAndAudits()
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email);
        var client = NewClient();

        await client.LoginAsync(email, TestData.DefaultPassword);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken);
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        using var cached = JsonDocument.Parse((string)(await redis.StringGetAsync(CacheKeys.Session(Guid.Parse(jwt.GetClaim("fid").Value))))!);
        Assert.Equal(int.Parse(jwt.GetClaim("sv").Value), cached.RootElement.GetProperty("sv").GetInt32());
        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.Login && a.ActorId == userId && a.Result == AuditResult.Succeeded)));
    }
}

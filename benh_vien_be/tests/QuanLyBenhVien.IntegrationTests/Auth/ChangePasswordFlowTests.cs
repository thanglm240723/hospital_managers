using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

/// Luồng đổi mật khẩu đầu-cuối gọi API trực tiếp (spec V2 §5.1, §5.3). Không phải bằng chứng Gateway từ chối token cũ.
[Collection(IntegrationCollection.Name)]
public class ChangePasswordFlowTests : IAsyncLifetime
{
    private const string NewPassword = "Brand-New-Pass-99";
    private const string Path = "/api/v1/auth/change-password";
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public ChangePasswordFlowTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid UserId, string Email, AuthTestClient Client)> LoggedInAsync(bool mustChangePassword = false)
    {
        var email = TestData.NewEmail("flow");
        var userId = await TestData.CreateUserAsync(_factory, email, mustChangePassword: mustChangePassword);
        return (userId, email, await LoginAsync(email, TestData.DefaultPassword));
    }

    private async Task<AuthTestClient> LoginAsync(string email, string password)
    {
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, password)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<HttpResponseMessage> ChangeAsync(AuthTestClient client, string current, string next,
        bool csrf = true, string? origin = TestConstants.Origin)
        => client.SendAsync(HttpMethod.Post, Path, new { currentPassword = current, newPassword = next }, csrf: csrf, origin: origin);

    private Task<(string Hash, int Sv)> UserStateAsync(Guid userId)
        => TestData.QueryAsync(_factory, async db =>
        {
            var u = await db.Users.AsNoTracking().SingleAsync(x => x.Id == userId);
            return (u.PasswordHash, u.SecurityVersion);
        });

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    private static string Claim(AuthTestClient client, string type)
        => new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim(type).Value;

    public static TheoryData<string, bool, string?> CsrfCases => new()
    {
        { "missing-token", false, TestConstants.Origin },
        { "foreign-origin", true, "https://evil.example" },
        { "missing-origin", true, null },
    };

    [Theory]
    [MemberData(nameof(CsrfCases))]
    public async Task CsrfOrOriginInvalid_Returns403_AndDoesNotTouchDb(string scenario, bool csrf, string? origin)
    {
        _ = scenario;
        var (userId, _, client) = await LoggedInAsync();
        var before = await UserStateAsync(userId);

        var response = await ChangeAsync(client, TestData.DefaultPassword, NewPassword, csrf, origin);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", (await BodyAsync(response)).GetProperty("code").GetString());
        Assert.Equal(before, await UserStateAsync(userId));
        Assert.False(await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.AnyAsync(a => a.ActorId == userId && a.Action == AuditActions.PasswordChange)));
    }

    [Fact]
    public async Task CsrfTokenOfAnotherFamily_Returns403()
    {
        var (userId, email, client) = await LoggedInAsync();
        var other = await LoginAsync(email, TestData.DefaultPassword);
        client.CsrfToken = other.CsrfToken;
        var before = await UserStateAsync(userId);

        Assert.Equal(HttpStatusCode.Forbidden, (await ChangeAsync(client, TestData.DefaultPassword, NewPassword)).StatusCode);
        Assert.Equal(before, await UserStateAsync(userId));
    }

    [Fact]
    public async Task WrongCurrentPassword_Returns400FieldError_AndCommitsFailedAudit()
    {
        var (userId, _, client) = await LoggedInAsync();
        var before = await UserStateAsync(userId);

        var response = await ChangeAsync(client, "Not-My-Password-1", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await BodyAsync(response)).GetProperty("errors").TryGetProperty("currentPassword", out _));
        Assert.Equal(before, await UserStateAsync(userId));
        var record = await TestData.QueryAsync(_factory, db => db.AuditRecords
            .SingleAsync(a => a.Action == AuditActions.PasswordChange && a.ActorId == userId && a.Result == AuditResult.Failed));
        Assert.Equal("InvalidCurrentPassword", record.Reason);
    }

    [Fact]
    public async Task NewPasswordTooShortSameAsCurrentOrContainingEmailName_Returns400OnNewPassword()
    {
        var (userId, email, client) = await LoggedInAsync();
        var local = email[..email.IndexOf('@')].ToUpperInvariant();
        var before = await UserStateAsync(userId);

        foreach (var next in new[] { "short", TestData.DefaultPassword, $"xx-{local}-9" })
        {
            var response = await ChangeAsync(client, TestData.DefaultPassword, next);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.True((await BodyAsync(response)).GetProperty("errors").TryGetProperty("newPassword", out _));
        }

        Assert.Equal(before, await UserStateAsync(userId));
    }

    [Fact]
    public async Task NoBearer_Returns401()
    {
        var (_, _, client) = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, Path,
            new { currentPassword = TestData.DefaultPassword, newPassword = NewPassword }, bearer: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task OtherSessionAfterChange_StaleSvAndRevokedFamily_Returns401()
    {
        var (userId, email, current) = await LoggedInAsync();
        var other = await LoginAsync(email, TestData.DefaultPassword);
        (await ChangeAsync(current, TestData.DefaultPassword, NewPassword)).EnsureSuccessStatusCode();
        var after = await UserStateAsync(userId);

        var response = await ChangeAsync(other, NewPassword, "Another-Pass-777");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(after, await UserStateAsync(userId));
    }

    [Fact]
    public async Task CurrentFamilyRevokedWithSameSv_Returns401()
    {
        var (userId, _, client) = await LoggedInAsync();
        var fid = Guid.Parse(Claim(client, "fid"));
        await TestData.QueryAsync(_factory, async db =>
        {
            var family = await db.SessionFamilies.SingleAsync(f => f.Id == fid);
            family.Revoke(SessionRevokeReason.UserRevoked, DateTimeOffset.UtcNow);
            return await db.SaveChangesAsync();
        });
        var before = await UserStateAsync(userId);

        Assert.Equal(HttpStatusCode.Unauthorized, (await ChangeAsync(client, TestData.DefaultPassword, NewPassword)).StatusCode);
        Assert.Equal(before, await UserStateAsync(userId));
    }

    [Fact]
    public async Task Success_UpdatesUserAndSessions_InvalidatesCache_AuditsAndReturnsOnlyAccessToken()
    {
        var (userId, email, current) = await LoggedInAsync(mustChangePassword: true);
        var other = await LoginAsync(email, TestData.DefaultPassword);
        var currentFid = Guid.Parse(Claim(current, "fid"));
        var otherFid = Guid.Parse(Claim(other, "fid"));
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        await redis.StringSetAsync(CacheKeys.Session(currentFid), "stale");
        await redis.StringSetAsync(CacheKeys.Session(otherFid), "stale");
        await redis.StringSetAsync(CacheKeys.Permissions(userId), "stale");
        var (_, oldSv) = await UserStateAsync(userId);
        var oldAccess = current.AccessToken;

        var response = await ChangeAsync(current, TestData.DefaultPassword, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"));
        var body = await BodyAsync(response);
        Assert.False(body.GetProperty("mustChangePassword").GetBoolean());
        Assert.True(body.TryGetProperty("expiresAtUtc", out _));
        Assert.NotEqual(oldAccess, current.AccessToken);
        Assert.Equal(oldSv + 1, int.Parse(Claim(current, "sv")));
        Assert.Equal(currentFid, Guid.Parse(Claim(current, "fid")));

        var user = await TestData.QueryAsync(_factory, db => db.Users.AsNoTracking().SingleAsync(u => u.Id == userId));
        Assert.True(_factory.Services.GetRequiredService<IPasswordHasher>().Verify(NewPassword, user.PasswordHash));
        Assert.False(user.MustChangePassword);
        Assert.Equal(oldSv + 1, user.SecurityVersion);

        var families = await TestData.QueryAsync(_factory, db => db.SessionFamilies.AsNoTracking()
            .Where(f => f.UserId == userId).ToDictionaryAsync(f => f.Id));
        Assert.Equal(SessionStatus.Active, families[currentFid].Status);
        Assert.Equal(SessionStatus.Revoked, families[otherFid].Status);
        Assert.Equal(SessionRevokeReason.PasswordChanged, families[otherFid].RevokeReason);

        // Invalidation ghi cùng transaction, flush sau commit: mọi key cũ (kể cả family hiện tại) và perm bị xoá.
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(currentFid)));
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(otherFid)));
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Permissions(userId)));

        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.PasswordChange && a.ActorId == userId && a.Result == AuditResult.Succeeded)));

        Assert.Equal(HttpStatusCode.OK, (await current.GetAsync("/api/v1/auth/me")).StatusCode);
        // Refresh endpoint thuộc task khác (hiện 501); trạng thái family ở DB đã kiểm ở trên.
        Assert.Equal(HttpStatusCode.OK, (await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task SixWrongCurrentPasswords_All400_NoLockout_SeventhCorrectSucceeds()
    {
        var (_, _, client) = await LoggedInAsync();

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await ChangeAsync(client, $"Wrong-Password-{i}", NewPassword)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await ChangeAsync(client, TestData.DefaultPassword, NewPassword)).StatusCode);
    }
}

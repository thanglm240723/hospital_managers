using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.RefreshSession;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using StackExchange.Redis;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

/// Task 1 (plan 03 refresh): rotate trong transaction, strict reuse commit trước khi trả lỗi — mức handler (MediatR) trên PostgreSQL/Redis thật.
[Collection(IntegrationCollection.Name)]
public class RefreshRotationTests
{
    private readonly ContainersFixture _containers;

    public RefreshRotationTests(ContainersFixture containers) => _containers = containers;

    private static Guid Fid(string accessToken)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim("fid").Value);

    private static int Sv(string accessToken)
        => int.Parse(new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim("sv").Value);

    private static async Task<(Guid UserId, AuthTestClient Client)> LoginAsync(ApiFactory api, string prefix = "refresh")
    {
        var email = TestData.NewEmail(prefix);
        var userId = await TestData.CreateUserAsync(api, email);
        var client = new AuthTestClient(api.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (userId, client);
    }

    private static async Task<Result<AuthTokensResult>> RefreshAsync(ApiFactory api, string token)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISender>().Send(new RefreshSessionCommand(token));
    }

    private static string Hash(ApiFactory api, string token)
    {
        using var scope = api.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<IRefreshTokenGenerator>().Hash(token);
    }

    private static Task<SessionFamily> FamilyAsync(ApiFactory api, Guid familyId)
        => TestData.QueryAsync(api, db => db.SessionFamilies.AsNoTracking().SingleAsync(f => f.Id == familyId));

    private static Task<List<RefreshToken>> TokensAsync(ApiFactory api, Guid familyId)
        => TestData.QueryAsync(api, db => db.RefreshTokens.AsNoTracking().Where(t => t.FamilyId == familyId).ToListAsync());

    private static Task<int> ReuseAuditsAsync(ApiFactory api, Guid userId)
        => TestData.QueryAsync(api, db => db.AuditRecords.AsNoTracking()
            .CountAsync(a => a.Action == AuditActions.RefreshReuse && a.ActorId == userId && a.Result == AuditResult.Denied));

    private static async Task<bool> SessionCachedAsync(ApiFactory api, Guid familyId)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
            .KeyExistsAsync(CacheKeys.Session(familyId));
    }

    private static async Task ExecAsync(ApiFactory api, FormattableString sql)
    {
        await using var scope = api.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<QuanLyBenhVien.Persistence.AppDbContext>().Database.ExecuteSqlInterpolatedAsync(sql);
    }

    [Fact]
    public async Task ValidToken_RotatesWithinSameFamily_WithoutExtendingAbsoluteExpiry()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var (userId, client) = await LoginAsync(api);
        var fid = Fid(client.AccessToken!);
        var before = await FamilyAsync(api, fid);

        var result = await RefreshAsync(api, client.RefreshToken!);

        Assert.True(result.IsSuccess);
        var tokens = result.Value;
        Assert.NotEqual(client.RefreshToken, tokens.RefreshToken);
        Assert.Equal(fid, Fid(tokens.AccessToken));
        Assert.Equal(before.AbsoluteExpiresAtUtc, tokens.SessionExpiresAtUtc);
        Assert.False(string.IsNullOrEmpty(tokens.CsrfToken));

        var after = await FamilyAsync(api, fid);
        Assert.Equal(SessionStatus.Active, after.Status);
        Assert.NotNull(after.LastRefreshedAtUtc);
        Assert.Equal(before.AbsoluteExpiresAtUtc, after.AbsoluteExpiresAtUtc);

        var rows = await TokensAsync(api, fid);
        var old = rows.Single(t => t.TokenHash == Hash(api, client.RefreshToken!));
        var fresh = rows.Single(t => t.TokenHash == Hash(api, tokens.RefreshToken));
        Assert.NotNull(old.ConsumedAtUtc);
        Assert.Equal(fresh.Id, old.ReplacedById);
        var usable = Assert.Single(rows, t => t.ConsumedAtUtc == null && t.RevokedAtUtc == null);
        Assert.Equal(fresh.Id, usable.Id);
        Assert.Equal(0, await ReuseAuditsAsync(api, userId));
    }

    [Fact]
    public async Task ConsumedTokenPresentedAgain_RevokesFamilyWithReuse_AuditAndInvalidationCommittedBefore401()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var (userId, client) = await LoginAsync(api);
        var fid = Fid(client.AccessToken!);
        var rotated = await RefreshAsync(api, client.RefreshToken!);
        Assert.True(rotated.IsSuccess);
        Assert.True(await SessionCachedAsync(api, fid));

        var result = await RefreshAsync(api, client.RefreshToken!);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
        var family = await FamilyAsync(api, fid);
        Assert.Equal(SessionStatus.Revoked, family.Status);
        Assert.Equal(SessionRevokeReason.Reuse, family.RevokeReason);
        Assert.DoesNotContain(await TokensAsync(api, fid), t => t.ConsumedAtUtc == null && t.RevokedAtUtc == null);
        Assert.Equal(1, await ReuseAuditsAsync(api, userId));
        Assert.False(await SessionCachedAsync(api, fid));

        // Token mới (đã bị revoke theo family) cũng không dùng được và không đổi reason.
        var again = await RefreshAsync(api, rotated.Value.RefreshToken);
        Assert.True(again.IsFailure);
        Assert.Equal(SessionRevokeReason.Reuse, (await FamilyAsync(api, fid)).RevokeReason);
        Assert.Equal(1, await ReuseAuditsAsync(api, userId));
    }

    [Fact]
    public async Task RevokedTokenOnActiveFamily_IsReuse_NotSilentlyNotFound()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var (userId, client) = await LoginAsync(api);
        var fid = Fid(client.AccessToken!);
        var hash = Hash(api, client.RefreshToken!);
        await ExecAsync(api, $"UPDATE \"RefreshTokens\" SET \"RevokedAtUtc\" = now() WHERE \"TokenHash\" = {hash}");

        var result = await RefreshAsync(api, client.RefreshToken!);

        Assert.True(result.IsFailure);
        var family = await FamilyAsync(api, fid);
        Assert.Equal(SessionStatus.Revoked, family.Status);
        Assert.Equal(SessionRevokeReason.Reuse, family.RevokeReason);
        Assert.Equal(1, await ReuseAuditsAsync(api, userId));
    }

    [Fact]
    public async Task LoggedOutFamily_ConsumedToken_KeepsLogoutReason_NoReuseAudit()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var (userId, client) = await LoginAsync(api);
        var fid = Fid(client.AccessToken!);
        var oldToken = client.RefreshToken!;
        Assert.True((await RefreshAsync(api, oldToken)).IsSuccess);
        await ExecAsync(api, $"UPDATE \"SessionFamilies\" SET \"Status\" = 'Revoked', \"RevokeReason\" = 'Logout', \"RevokedAtUtc\" = now() WHERE \"Id\" = {fid}");

        var result = await RefreshAsync(api, oldToken);

        Assert.True(result.IsFailure);
        Assert.Equal(SessionRevokeReason.Logout, (await FamilyAsync(api, fid)).RevokeReason);
        Assert.Equal(0, await ReuseAuditsAsync(api, userId));
    }

    [Fact]
    public async Task ExpiredFamily_OrInactiveUser_OrGarbage_DoesNotRotate()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);

        var (_, expired) = await LoginAsync(api);
        var expiredFid = Fid(expired.AccessToken!);
        await ExecAsync(api, $"UPDATE \"SessionFamilies\" SET \"AbsoluteExpiresAtUtc\" = now() - interval '1 minute' WHERE \"Id\" = {expiredFid}");
        Assert.True((await RefreshAsync(api, expired.RefreshToken!)).IsFailure);
        Assert.Single(await TokensAsync(api, expiredFid));

        var (inactiveUserId, inactive) = await LoginAsync(api);
        var inactiveFid = Fid(inactive.AccessToken!);
        await ExecAsync(api, $"UPDATE \"Users\" SET \"IsActive\" = false WHERE \"Id\" = {inactiveUserId}");
        Assert.True((await RefreshAsync(api, inactive.RefreshToken!)).IsFailure);
        var inactiveTokens = await TokensAsync(api, inactiveFid);
        Assert.Single(inactiveTokens);
        Assert.Null(inactiveTokens[0].ConsumedAtUtc);

        foreach (var garbage in new[] { "", "not-a-real-token" })
        {
            var r = await RefreshAsync(api, garbage);
            Assert.True(r.IsFailure);
            Assert.Equal(ErrorType.Unauthorized, r.Error!.Type);
        }
    }

    [Fact]
    public async Task ParallelRefreshWithSameToken_ExactlyOneSucceeds_OthersTriggerReuse()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var (_, client) = await LoginAsync(api);
        var fid = Fid(client.AccessToken!);

        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => RefreshAsync(api, client.RefreshToken!)));

        Assert.Single(results, r => r.IsSuccess);
        var family = await FamilyAsync(api, fid);
        Assert.Equal(SessionStatus.Revoked, family.Status);
        Assert.Equal(SessionRevokeReason.Reuse, family.RevokeReason);
    }

    /// Đổi mật khẩu giữ khoá User (cùng thứ tự User → family) trong khi refresh chờ: refresh phải thấy sv MỚI sau khi khoá nhả.
    [Fact]
    public async Task RefreshBlockedBehindUserLock_IssuesAccessTokenWithLatestSecurityVersion()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var (userId, client) = await LoginAsync(api);
        var oldSv = Sv(client.AccessToken!);

        await using var conn = new NpgsqlConnection(api.DatabaseConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand("SELECT 1 FROM \"Users\" WHERE \"Id\" = @id FOR UPDATE", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", userId);
            await cmd.ExecuteNonQueryAsync();
        }
        await using (var cmd = new NpgsqlCommand("UPDATE \"Users\" SET \"SecurityVersion\" = \"SecurityVersion\" + 1 WHERE \"Id\" = @id", conn, tx))
        {
            cmd.Parameters.AddWithValue("id", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        var refresh = RefreshAsync(api, client.RefreshToken!);
        await WaitUntilBlockedOnLockAsync(api.DatabaseConnectionString, TimeSpan.FromSeconds(10));
        Assert.False(refresh.IsCompleted, "Refresh phải còn bị chặn bởi khoá User.");

        await tx.CommitAsync();
        var result = await refresh.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.True(result.IsSuccess);
        Assert.Equal(oldSv + 1, Sv(result.Value.AccessToken));
    }

    private static async Task WaitUntilBlockedOnLockAsync(string connectionString, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(deadline.Token);
                await using var cmd = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()", conn);
                if ((long)(await cmd.ExecuteScalarAsync(deadline.Token))! > 0) return;
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"Không thấy phiên nào bị chặn bởi khoá hàng trong {timeout}.");
        }
    }
}

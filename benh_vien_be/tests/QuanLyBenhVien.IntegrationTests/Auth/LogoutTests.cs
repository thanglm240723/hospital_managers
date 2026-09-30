using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Npgsql;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Identity.Sessions;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using QuanLyBenhVien.Persistence;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

/// Task 2 (plan 02 logout): thu hồi family khi logout/logout-all — kiểm trạng thái PostgreSQL thật, không dựa vào /refresh.
[Collection(IntegrationCollection.Name)]
public class LogoutTests
{
    private const string Logout = "/api/v1/auth/logout";
    private const string LogoutAll = "/api/v1/auth/logout-all";
    private readonly ContainersFixture _containers;

    public LogoutTests(ContainersFixture containers) => _containers = containers;

    private static Guid Fid(AuthTestClient client)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim("fid").Value);

    private static async Task<AuthTestClient> LoginAsync(ApiFactory api, string email)
    {
        var client = new AuthTestClient(api.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static Task<SessionStatus> StatusAsync(ApiFactory api, Guid familyId)
        => TestData.QueryAsync(api, db => db.SessionFamilies.AsNoTracking().Where(f => f.Id == familyId).Select(f => f.Status).SingleAsync());

    private static Task<SessionRevokeReason?> ReasonAsync(ApiFactory api, Guid familyId)
        => TestData.QueryAsync(api, db => db.SessionFamilies.AsNoTracking().Where(f => f.Id == familyId).Select(f => f.RevokeReason).SingleAsync());

    private static Task<DateTimeOffset?> RevokedAtAsync(ApiFactory api, Guid familyId)
        => TestData.QueryAsync(api, db => db.SessionFamilies.AsNoTracking().Where(f => f.Id == familyId).Select(f => f.RevokedAtUtc).SingleAsync());

    private static Task<int> UsableTokensAsync(ApiFactory api, Guid familyId)
        => TestData.QueryAsync(api, db => db.RefreshTokens.AsNoTracking()
            .CountAsync(t => t.FamilyId == familyId && t.RevokedAtUtc == null && t.ConsumedAtUtc == null));

    private static Task<int> AuditsAsync(ApiFactory api, string action, Guid userId)
        => TestData.QueryAsync(api, db => db.AuditRecords.AsNoTracking().CountAsync(a => a.Action == action && a.ActorId == userId));

    [Fact]
    public async Task Logout_RevokesOnlyCurrentFamily_OtherFamiliesStayActive()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var email = TestData.NewEmail("logout");
        var userId = await TestData.CreateUserAsync(api, email);
        var a = await LoginAsync(api, email);
        var b = await LoginAsync(api, email);

        var response = await a.SendAsync(HttpMethod.Post, Logout, bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(a.RefreshToken);
        Assert.Null(a.CsrfToken);
        Assert.Equal(SessionStatus.Revoked, await StatusAsync(api, Fid(a)));
        Assert.Equal(SessionRevokeReason.Logout, await ReasonAsync(api, Fid(a)));
        Assert.Equal(0, await UsableTokensAsync(api, Fid(a)));
        Assert.Equal(SessionStatus.Active, await StatusAsync(api, Fid(b)));
        Assert.Equal(1, await AuditsAsync(api, AuditActions.Logout, userId));
    }

    [Fact]
    public async Task Logout_Repeated_Returns204_NoDuplicateAuditOrChange()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var email = TestData.NewEmail("logout");
        var userId = await TestData.CreateUserAsync(api, email);
        var client = await LoginAsync(api, email);
        var refresh = client.RefreshToken;
        var csrf = client.CsrfToken;
        (await client.SendAsync(HttpMethod.Post, Logout, bearer: false)).EnsureSuccessStatusCode();
        var revokedAt = await RevokedAtAsync(api, Fid(client));

        client.RefreshToken = refresh;
        client.CsrfToken = csrf;
        var again = await client.SendAsync(HttpMethod.Post, Logout, bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        Assert.Null(client.RefreshToken);
        Assert.Equal(1, await AuditsAsync(api, AuditActions.Logout, userId));
        Assert.Equal(revokedAt, await RevokedAtAsync(api, Fid(client)));
    }

    [Fact]
    public async Task Logout_DatabaseFailsOnSave_RollsBack_DoesNotReturn204()
    {
        var failSwitch = new FailSwitch();
        await using var api = await ApiFactory.CreateAsync(_containers, configureServices: s =>
            s.Replace(ServiceDescriptor.Scoped<IUnitOfWork>(sp => new FailingUnitOfWork(sp.GetRequiredService<AppDbContext>(), failSwitch))));
        var email = TestData.NewEmail("logout");
        var userId = await TestData.CreateUserAsync(api, email);
        var client = await LoginAsync(api, email);
        failSwitch.On = true;

        var response = await client.SendAsync(HttpMethod.Post, Logout, bearer: false);

        Assert.False(response.IsSuccessStatusCode, $"Không được trả thành công khi DB lỗi: {(int)response.StatusCode}");
        Assert.NotNull(client.RefreshToken);
        Assert.Equal(SessionStatus.Active, await StatusAsync(api, Fid(client)));
        Assert.Equal(0, await AuditsAsync(api, AuditActions.Logout, userId));
    }

    [Fact]
    public async Task LogoutAll_RevokesEveryActiveFamilyOfCurrentUser_NotOtherUsers()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var email = TestData.NewEmail("logout-all");
        var userId = await TestData.CreateUserAsync(api, email);
        var otherEmail = TestData.NewEmail("bystander");
        await TestData.CreateUserAsync(api, otherEmail);
        var a = await LoginAsync(api, email);
        var b = await LoginAsync(api, email);
        var other = await LoginAsync(api, otherEmail);

        var response = await a.SendAsync(HttpMethod.Post, LogoutAll);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(a.RefreshToken);
        Assert.Null(a.CsrfToken);
        foreach (var fid in new[] { Fid(a), Fid(b) })
        {
            Assert.Equal(SessionStatus.Revoked, await StatusAsync(api, fid));
            Assert.Equal(SessionRevokeReason.LogoutAll, await ReasonAsync(api, fid));
            Assert.Equal(0, await UsableTokensAsync(api, fid));
        }

        Assert.Equal(SessionStatus.Active, await StatusAsync(api, Fid(other)));
        Assert.Equal(1, await AuditsAsync(api, AuditActions.LogoutAll, userId));
    }

    [Fact]
    public async Task LogoutAll_WithoutCsrf_Returns403_NothingRevoked()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var email = TestData.NewEmail("logout-all");
        await TestData.CreateUserAsync(api, email);
        var client = await LoginAsync(api, email);

        var response = await client.SendAsync(HttpMethod.Post, LogoutAll, csrf: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(SessionStatus.Active, await StatusAsync(api, Fid(client)));
    }

    [Fact]
    public async Task LogoutAll_WithoutBearer_Returns401_NothingRevoked()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var email = TestData.NewEmail("logout-all");
        await TestData.CreateUserAsync(api, email);
        var client = await LoginAsync(api, email);

        var response = await client.SendAsync(HttpMethod.Post, LogoutAll, bearer: false);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(SessionStatus.Active, await StatusAsync(api, Fid(client)));
    }

    [Fact]
    public async Task Race_LoginQueuedBeforeLogoutAll_LoginFamilyIsRevoked_LaterLoginStaysActive()
    {
        await using var api = await ApiFactory.CreateAsync(_containers);
        var email = TestData.NewEmail("race");
        var userId = await TestData.CreateUserAsync(api, email);
        var current = await LoginAsync(api, email);

        // Barrier: giữ khoá hàng User ⇒ login rồi logout-all xếp hàng chờ theo đúng thứ tự khoá.
        await using var holder = new NpgsqlConnection(api.DatabaseConnectionString);
        await holder.OpenAsync();
        await using var tx = await holder.BeginTransactionAsync();
        await using (var cmd = new NpgsqlCommand("SELECT 1 FROM \"Users\" WHERE \"Id\" = @id FOR UPDATE", holder, tx))
        {
            cmd.Parameters.AddWithValue("id", userId);
            await cmd.ExecuteNonQueryAsync();
        }

        var racer = new AuthTestClient(api.CreateHttpsClient());
        var loginTask = racer.LoginAsync(email, TestData.DefaultPassword);
        await WaitUntilBlockedAsync(api.DatabaseConnectionString, expected: 1, TimeSpan.FromSeconds(10));
        var logoutAllTask = current.SendAsync(HttpMethod.Post, LogoutAll);
        await WaitUntilBlockedAsync(api.DatabaseConnectionString, expected: 2, TimeSpan.FromSeconds(10));
        await tx.CommitAsync();

        var login = await loginTask;
        var logoutAll = await logoutAllTask;

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, logoutAll.StatusCode);
        Assert.Equal(SessionStatus.Revoked, await StatusAsync(api, Fid(racer)));
        Assert.Equal(SessionStatus.Revoked, await StatusAsync(api, Fid(current)));

        // Không "cấm login mãi": login sau logout-all tạo phiên mới hợp lệ.
        var later = await LoginAsync(api, email);
        Assert.Equal(SessionStatus.Active, await StatusAsync(api, Fid(later)));
    }

    private static async Task WaitUntilBlockedAsync(string connectionString, int expected, TimeSpan timeout)
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
                if ((long)(await cmd.ExecuteScalarAsync(deadline.Token))! >= expected) return;
                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException($"Không thấy {expected} phiên bị chặn bởi khoá hàng trong {timeout}.");
        }
    }

    private sealed class FailSwitch
    {
        public volatile bool On;
    }

    private sealed class FailingUnitOfWork(AppDbContext inner, FailSwitch failSwitch) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
            => failSwitch.On ? throw new InvalidOperationException("mô phỏng lỗi CSDL (test)") : inner.SaveChangesAsync(ct);

        public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) => inner.BeginTransactionAsync(ct);
    }
}

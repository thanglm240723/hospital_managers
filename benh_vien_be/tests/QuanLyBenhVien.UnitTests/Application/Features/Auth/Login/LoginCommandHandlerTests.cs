using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Application.Features.Auth.Login;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Auth.Login;

file sealed class FakeUserRepository : IUserRepository
{
    public User? User { get; set; }
    public bool GetByEmailCalled { get; private set; }

    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        GetByEmailCalled = true;
        return Task.FromResult(User);
    }

    public Task<User> GetUserByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(User!);
    public Task AddUserAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
    public void UpdateUser(User user) { }
    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(false);
    public Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default) => Task.FromResult(User);
    public Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default) => Task.FromResult(0);
    public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task AcquireAdminSafetyLockAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeSessionRepository : ISessionRepository
{
    public SessionFamily? Added { get; private set; }

    public Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
    public Task<(Guid FamilyId, SessionStatus Status)?> FindFamilyStatusByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult<(Guid, SessionStatus)?>(null);
    public Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default) => Task.FromResult<SessionFamily?>(null);
    public Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<SessionFamily>>([]);

    public Task AddAsync(SessionFamily family, CancellationToken ct = default)
    {
        Added = family;
        return Task.CompletedTask;
    }
}

file sealed class FakeUnitOfWork : IUnitOfWork
{
    public List<string>? Timeline { get; set; }
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        Timeline?.Add("save-changes");
        return Task.FromResult(1);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => throw new NotSupportedException("LoginCommandHandler không dùng transaction tường minh.");
}

file sealed class FakePasswordHasher : IPasswordHasher
{
    public bool VerifyResult { get; set; }
    public bool SimulateVerifyCalled { get; private set; }

    public string Hash(string password) => "hash:" + password;
    public bool Verify(string password, string passwordHash) => VerifyResult;
    public void SimulateVerify(string password) => SimulateVerifyCalled = true;
}

file sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public AccessToken Issue(Guid userId, Guid sessionFamilyId, int securityVersion)
        => new("access-token", DateTimeOffset.UtcNow.AddMinutes(15));
}

file sealed class FakeRefreshTokenGenerator : IRefreshTokenGenerator
{
    public GeneratedRefreshToken Generate() => new("refresh-token", "refresh-hash");
    public string Hash(string token) => "refresh-hash";
}

file sealed class FakeCsrfTokenService : ICsrfTokenService
{
    public Guid? CreatedFor { get; private set; }
    public string Create(Guid sessionFamilyId)
    {
        CreatedFor = sessionFamilyId;
        return "csrf-token";
    }
    public bool IsValid(Guid sessionFamilyId, string? token) => true;
}

file sealed class FakeLoginAttemptLimiter : ILoginAttemptLimiter
{
    public List<string>? Timeline { get; set; }
    public TimeSpan? LockoutRemaining { get; set; }
    public bool RegisterFailureCalled { get; private set; }
    public bool ResetCalled { get; private set; }

    public Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, CancellationToken ct = default) => Task.FromResult(LockoutRemaining);
    public Task RegisterFailureAsync(string normalizedEmail, CancellationToken ct = default)
    {
        RegisterFailureCalled = true;
        return Task.CompletedTask;
    }
    public Task ResetAsync(string normalizedEmail, CancellationToken ct = default)
    {
        ResetCalled = true;
        Timeline?.Add("reset");
        return Task.CompletedTask;
    }
}

file sealed class FakeSessionCache : ISessionCache
{
    public List<string>? Timeline { get; set; }
    public SessionCacheEntry? WrittenEntry { get; private set; }
    public CacheGeneration? WrittenExpected { get; private set; }

    public Task<CacheGeneration?> ReadGenerationAsync(Guid sessionFamilyId, CancellationToken ct = default) => Task.FromResult<CacheGeneration?>(null);

    public Task SetIfGenerationUnchangedAsync(SessionCacheEntry entry, CacheGeneration expected, CancellationToken ct = default)
    {
        WrittenEntry = entry;
        WrittenExpected = expected;
        Timeline?.Add("write-cache");
        return Task.CompletedTask;
    }
}

file sealed record AuditCall(string Action, AuditResult Result, string? Reason, Guid? ActorId);

file sealed class FakeAuditWriter : IAuditWriter
{
    public List<AuditCall> Calls { get; } = [];

    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)
        => Calls.Add(new AuditCall(action, result, reason, actorId));
}

file sealed class FakeRequestContext : IRequestContext
{
    public string? CorrelationId => "corr-1";
    public string? IpAddress => "10.0.0.1";
    public string? UserAgent => "UA";
}

file sealed class Fixture
{
    public static readonly DateTimeOffset Now = new(2026, 9, 28, 8, 0, 0, TimeSpan.Zero);

    /// Dòng thời gian dùng chung giữa các fake ghi sau khi đăng nhập thành công — chứng minh thứ tự
    /// commit DB TRƯỚC khi làm việc phụ (reset rate limiter, ghi cache), không phải chỉ độc lập đều đã xảy ra.
    public List<string> Timeline { get; } = [];

    public FakeUserRepository Users { get; } = new();
    public FakeSessionRepository Sessions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; }
    public FakePasswordHasher Hasher { get; } = new();
    public FakeLoginAttemptLimiter Limiter { get; }
    public FakeSessionCache SessionCache { get; }
    public FakeAuditWriter Audit { get; } = new();
    public FakeCsrfTokenService Csrf { get; } = new();

    public Fixture()
    {
        UnitOfWork = new FakeUnitOfWork { Timeline = Timeline };
        Limiter = new FakeLoginAttemptLimiter { Timeline = Timeline };
        SessionCache = new FakeSessionCache { Timeline = Timeline };
    }

    public LoginCommandHandler CreateHandler() => new(
        Users, Sessions, UnitOfWork, Hasher, new FakeAccessTokenIssuer(), new FakeRefreshTokenGenerator(),
        Csrf, Limiter, SessionCache, Audit, new FakeRequestContext(), new FakeTimeProvider(Now));
}

public class LoginCommandHandlerTests
{
    private static readonly DateTimeOffset Now = Fixture.Now;

    // (a) bị chặn bởi rate limiter
    [Fact]
    public async Task LockedOut_ReturnsTooManyAttempts_AuditsRateLimited_DoesNotLookUpUser()
    {
        var f = new Fixture();
        f.Limiter.LockoutRemaining = TimeSpan.FromMinutes(10);

        var result = await f.CreateHandler().Handle(new LoginCommand("a@b.vn", "pw"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("rate_limited", result.Error!.Code);
        Assert.Equal(TimeSpan.FromMinutes(10), result.Error.RetryAfter);
        Assert.Single(f.Audit.Calls, c => c.Action == AuditActions.RateLimited && c.Result == AuditResult.Denied);
        Assert.Equal(1, f.UnitOfWork.SaveChangesCallCount);
        Assert.False(f.Users.GetByEmailCalled);
    }

    // (b) email không tồn tại
    [Fact]
    public async Task UnknownEmail_SimulatesVerify_RegistersFailure_AuditsWithNullActor()
    {
        var f = new Fixture();
        f.Users.User = null;

        var result = await f.CreateHandler().Handle(new LoginCommand("nope@b.vn", "pw"), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("unauthenticated", result.Error!.Code);
        Assert.True(f.Hasher.SimulateVerifyCalled);
        Assert.True(f.Limiter.RegisterFailureCalled);
        var call = Assert.Single(f.Audit.Calls);
        Assert.Equal("EmailNotFound", call.Reason);
        Assert.Null(call.ActorId);
        Assert.Equal(1, f.UnitOfWork.SaveChangesCallCount);
    }

    // (c) sai mật khẩu
    [Fact]
    public async Task WrongPassword_AuditsWithActorId()
    {
        var f = new Fixture();
        var user = User.Create("A", "a@b.vn", "hash", null);
        f.Users.User = user;
        f.Hasher.VerifyResult = false;

        var result = await f.CreateHandler().Handle(new LoginCommand("a@b.vn", "wrong"), CancellationToken.None);

        Assert.True(result.IsFailure);
        var call = Assert.Single(f.Audit.Calls);
        Assert.Equal("InvalidPassword", call.Reason);
        Assert.Equal(user.Id, call.ActorId);
    }

    // (d) tài khoản khoá + đúng mật khẩu
    [Fact]
    public async Task InactiveAccount_CorrectPassword_ReasonIsAccountInactive()
    {
        var f = new Fixture();
        var user = User.Create("A", "a@b.vn", "hash", null);
        user.Deactivate();
        f.Users.User = user;
        f.Hasher.VerifyResult = true;

        await f.CreateHandler().Handle(new LoginCommand("a@b.vn", "correct"), CancellationToken.None);

        Assert.Equal("AccountInactive", Assert.Single(f.Audit.Calls).Reason);
    }

    // (e) tài khoản khoá + sai mật khẩu -> vẫn báo sai mật khẩu (không lộ trạng thái khoá trước)
    [Fact]
    public async Task InactiveAccount_WrongPassword_ReasonIsInvalidPassword()
    {
        var f = new Fixture();
        var user = User.Create("A", "a@b.vn", "hash", null);
        user.Deactivate();
        f.Users.User = user;
        f.Hasher.VerifyResult = false;

        await f.CreateHandler().Handle(new LoginCommand("a@b.vn", "wrong"), CancellationToken.None);

        Assert.Equal("InvalidPassword", Assert.Single(f.Audit.Calls).Reason);
    }

    // (f) thành công
    [Fact]
    public async Task Success_SavesOnce_RecordsLastLogin_ResetsLimiterAndWritesCache_AfterCommit()
    {
        var f = new Fixture();
        var user = User.Create("A", "a@b.vn", "hash", null);
        f.Users.User = user;
        f.Hasher.VerifyResult = true;

        var result = await f.CreateHandler().Handle(new LoginCommand("a@b.vn", "correct"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, f.UnitOfWork.SaveChangesCallCount);
        Assert.Equal(Now, user.LastLoginAt);
        Assert.NotNull(f.Sessions.Added);
        Assert.Equal(Now.AddDays(7), f.Sessions.Added!.AbsoluteExpiresAtUtc);
        Assert.True(f.Limiter.ResetCalled);
        Assert.NotNull(f.SessionCache.WrittenEntry);
        Assert.Equal(CacheGeneration.None, f.SessionCache.WrittenExpected);
        Assert.Equal(f.Csrf.CreatedFor, f.Sessions.Added.Id);
        Assert.Single(f.Audit.Calls, c => c.Result == AuditResult.Succeeded);

        // "save-changes" (commit DB: user + session family) phải đứng trước "reset" (rate limiter) và
        // "write-cache" (Redis) — không được reset/ghi cache trước khi chắc chắn đã commit thành công.
        Assert.Equal(["save-changes", "reset", "write-cache"], f.Timeline);
    }
}

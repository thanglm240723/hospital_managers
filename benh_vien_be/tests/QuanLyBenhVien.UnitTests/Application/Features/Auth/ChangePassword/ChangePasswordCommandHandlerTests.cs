using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Application.Features.Auth.ChangePassword;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Domain.Identity.Sessions;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Auth.ChangePassword;

file sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; }
    public Guid? SessionFamilyId { get; set; }
    public int? SecurityVersion { get; set; }
}

file sealed class FakeUserRepository : IUserRepository
{
    public User? User { get; set; }
    public Task<User?> GetForUpdateAsync(Guid id, CancellationToken ct = default) => Task.FromResult(User?.Id == id ? User : null);
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult(User);
    public Task<User> GetUserByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(User!);
    public Task AddUserAsync(User user, CancellationToken ct = default) => Task.CompletedTask;
    public void UpdateUser(User user) { }
    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(false);
    public Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default) => Task.FromResult(User);
    public Task<User?> GetWithAccessForUpdateAsync(Guid id, CancellationToken ct = default) => Task.FromResult(User);
    public Task<IReadOnlyList<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default) => Task.FromResult(0);
    public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task AcquireAdminSafetyLockAsync(CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeSessionRepository : ISessionRepository
{
    public List<SessionFamily> Families { get; } = [];
    public Task<Guid?> FindFamilyIdByTokenHashAsync(string tokenHash, CancellationToken ct = default) => Task.FromResult<Guid?>(null);
    public Task<(Guid FamilyId, SessionStatus Status)?> FindFamilyStatusByTokenHashAsync(string tokenHash, CancellationToken ct = default)
        => Task.FromResult<(Guid, SessionStatus)?>(null);
    public Task<SessionFamily?> GetForUpdateAsync(Guid familyId, string? presentedTokenHash, CancellationToken ct = default)
        => Task.FromResult(Families.SingleOrDefault(f => f.Id == familyId));
    public Task<IReadOnlyList<SessionFamily>> GetActiveByUserForUpdateAsync(Guid userId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionFamily>>(Families.Where(f => f.UserId == userId && f.Status == SessionStatus.Active).OrderBy(f => f.Id).ToList());
    public Task AddAsync(SessionFamily family, CancellationToken ct = default) => Task.CompletedTask;
}

file sealed class FakeTransaction(List<string> timeline) : IUnitOfWorkTransaction
{
    public Task CommitAsync(CancellationToken ct = default)
    {
        // Mô phỏng EF/Npgsql: token đã huỷ ⇒ commit không xảy ra.
        ct.ThrowIfCancellationRequested();
        timeline.Add("commit");
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        timeline.Add("dispose-transaction");
        return ValueTask.CompletedTask;
    }
}

file sealed class FakeUnitOfWork(List<string> timeline) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        timeline.Add("save");
        return Task.FromResult(1);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default)
    {
        timeline.Add("begin");
        return Task.FromResult<IUnitOfWorkTransaction>(new FakeTransaction(timeline));
    }
}

file sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hash:" + password;
    public bool Verify(string password, string passwordHash) => passwordHash == "hash:" + password;
    public void SimulateVerify(string password) { }
}

file sealed class FakeAccessTokenIssuer : IAccessTokenIssuer
{
    public (Guid UserId, Guid FamilyId, int Sv)? Issued { get; private set; }

    public AccessToken Issue(Guid userId, Guid sessionFamilyId, int securityVersion)
    {
        Issued = (userId, sessionFamilyId, securityVersion);
        return new AccessToken("access-token", Fixture.Now.AddMinutes(15));
    }
}

file sealed class FakeCacheInvalidator(List<string> timeline) : ICacheInvalidator
{
    public List<Guid> Sessions { get; } = [];
    public List<Guid> Permissions { get; } = [];
    public Exception? FlushFailure { get; set; }

    public void InvalidateSession(Guid sessionFamilyId) => Sessions.Add(sessionFamilyId);
    public void InvalidatePermissions(Guid userId) => Permissions.Add(userId);

    public Task FlushAsync(CancellationToken ct = default)
    {
        timeline.Add("flush");
        return FlushFailure is null ? Task.CompletedTask : Task.FromException(FlushFailure);
    }
}

file sealed record AuditCall(string Action, AuditResult Result, string? Reason, Guid? ActorId);

file sealed class FakeAuditWriter(List<string> timeline) : IAuditWriter
{
    public List<AuditCall> Calls { get; } = [];

    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        timeline.Add("audit");
        Calls.Add(new AuditCall(action, result, reason, actorId));
    }
}

file sealed class Fixture
{
    public static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
    public const string CurrentPassword = "Old-Password-1";

    public List<string> Timeline { get; } = [];
    public FakeCurrentUser CurrentUser { get; } = new();
    public FakeUserRepository Users { get; } = new();
    public FakeSessionRepository Sessions { get; } = new();
    public FakeUnitOfWork UnitOfWork { get; }
    public FakeAccessTokenIssuer Tokens { get; } = new();
    public FakeCacheInvalidator Invalidator { get; }
    public FakeAuditWriter Audit { get; }
    public FakeTimeProvider Time { get; } = new(Now);
    public User User { get; }
    public SessionFamily Current { get; }
    public SessionFamily Other { get; }

    public Fixture()
    {
        UnitOfWork = new FakeUnitOfWork(Timeline);
        Invalidator = new FakeCacheInvalidator(Timeline);
        Audit = new FakeAuditWriter(Timeline);

        User = User.Create("Nguyen Van A", "bacsi.an@test.local", "hash:" + CurrentPassword, null);
        Users.User = User;
        Current = SessionFamily.Start(User.Id, "rt-1", Now.AddHours(-1), null, null);
        Other = SessionFamily.Start(User.Id, "rt-2", Now.AddHours(-1), null, null);
        Sessions.Families.AddRange([Current, Other]);

        CurrentUser.UserId = User.Id;
        CurrentUser.SessionFamilyId = Current.Id;
        CurrentUser.SecurityVersion = User.SecurityVersion;
    }

    public ChangePasswordCommandHandler CreateHandler() => new(
        CurrentUser, Users, Sessions, UnitOfWork, new FakePasswordHasher(), Tokens, Invalidator, Audit, Time,
        NullLogger<ChangePasswordCommandHandler>.Instance);
}

public class ChangePasswordCommandHandlerTests
{
    private const string NewPassword = "Brand-New-Pass-99";

    [Fact]
    public async Task Success_ChangesPassword_RevokesOthers_InvalidatesAll_CommitsBeforeFlush_IssuesNewSv()
    {
        var f = new Fixture();
        var oldSv = f.User.SecurityVersion;

        var result = await f.CreateHandler().Handle(new ChangePasswordCommand(Fixture.CurrentPassword, NewPassword), default);

        Assert.True(result.IsSuccess);
        Assert.Equal("access-token", result.Value.AccessToken);
        Assert.False(result.Value.MustChangePassword);
        Assert.Equal("hash:" + NewPassword, f.User.PasswordHash);
        Assert.False(f.User.MustChangePassword);
        Assert.Equal(oldSv + 1, f.User.SecurityVersion);
        Assert.Equal((f.User.Id, f.Current.Id, oldSv + 1), f.Tokens.Issued);

        Assert.Equal(SessionStatus.Active, f.Current.Status);
        Assert.Equal(SessionStatus.Revoked, f.Other.Status);
        Assert.Equal(SessionRevokeReason.PasswordChanged, f.Other.RevokeReason);

        Assert.Equal(new[] { f.Current.Id, f.Other.Id }.Order(), f.Invalidator.Sessions.Order());
        Assert.Equal([f.User.Id], f.Invalidator.Permissions);

        var audit = Assert.Single(f.Audit.Calls);
        Assert.Equal((AuditActions.PasswordChange, AuditResult.Succeeded, f.User.Id), (audit.Action, audit.Result, audit.ActorId));

        Assert.Equal(["begin", "audit", "save", "commit", "dispose-transaction", "flush"], f.Timeline);
    }

    [Fact]
    public async Task FlushFailureAfterCommit_StillSucceeds()
    {
        var f = new Fixture();
        f.Invalidator.FlushFailure = new OperationCanceledException();

        var result = await f.CreateHandler().Handle(new ChangePasswordCommand(Fixture.CurrentPassword, NewPassword), default);

        Assert.True(result.IsSuccess);
        Assert.Contains("commit", f.Timeline);
    }

    [Fact]
    public async Task WrongCurrentPassword_AuditsFailedAndCommits_ReturnsCurrentPasswordFieldError()
    {
        var f = new Fixture();
        var oldHash = f.User.PasswordHash;

        var result = await f.CreateHandler().Handle(new ChangePasswordCommand("Wrong-Password-1", NewPassword), default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.True(result.Error.FieldErrors!.ContainsKey("currentPassword"));
        var audit = Assert.Single(f.Audit.Calls);
        Assert.Equal((AuditActions.PasswordChange, AuditResult.Failed, "InvalidCurrentPassword"), (audit.Action, audit.Result, audit.Reason));
        Assert.Equal(["begin", "audit", "save", "commit", "dispose-transaction"], f.Timeline);
        Assert.Equal(oldHash, f.User.PasswordHash);
        Assert.Empty(f.Invalidator.Sessions);
        Assert.Equal(SessionStatus.Active, f.Other.Status);
    }

    [Theory]
    [InlineData(Fixture.CurrentPassword)]
    [InlineData("xx-BACSI.AN-2026")]
    public async Task InvalidNewPassword_ReturnsNewPasswordFieldError_WithoutCommit(string next)
    {
        var f = new Fixture();

        var result = await f.CreateHandler().Handle(new ChangePasswordCommand(Fixture.CurrentPassword, next), default);

        Assert.True(result.IsFailure);
        Assert.True(result.Error!.FieldErrors!.ContainsKey("newPassword"));
        Assert.DoesNotContain("commit", f.Timeline);
        Assert.Equal(1, f.User.SecurityVersion);
    }

    public static TheoryData<string> InvalidSessionCases => new() { "no-claims", "stale-sv", "revoked", "expired", "foreign", "inactive" };

    [Theory]
    [MemberData(nameof(InvalidSessionCases))]
    public async Task InvalidSession_ReturnsUnauthenticated_WithoutCommit(string scenario)
    {
        var f = new Fixture();
        switch (scenario)
        {
            case "no-claims": f.CurrentUser.SessionFamilyId = null; break;
            case "stale-sv": f.CurrentUser.SecurityVersion = f.User.SecurityVersion - 1; break;
            case "revoked": f.Current.Revoke(SessionRevokeReason.UserRevoked, Fixture.Now); break;
            case "expired": f.Time.SetUtcNow(f.Current.AbsoluteExpiresAtUtc); break;
            case "foreign": f.CurrentUser.SessionFamilyId = Guid.NewGuid(); break;
            case "inactive": f.User.Deactivate(); f.CurrentUser.SecurityVersion = f.User.SecurityVersion; break;
        }

        var result = await f.CreateHandler().Handle(new ChangePasswordCommand(Fixture.CurrentPassword, NewPassword), default);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Unauthorized, result.Error!.Type);
        Assert.DoesNotContain("commit", f.Timeline);
        Assert.Equal("hash:" + Fixture.CurrentPassword, f.User.PasswordHash);
    }

    // Client ngắt kết nối sau khi đã khóa/kiểm tra: quyết định ghi đã chốt thì Save/Commit không được bị huỷ theo request.
    [Theory]
    [InlineData(Fixture.CurrentPassword, "succeeded")]
    [InlineData("Wrong-Password-1", "failed")]
    public async Task RequestCancelledAfterDecision_StillSavesAndCommits(string current, string outcome)
    {
        var f = new Fixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await f.CreateHandler().Handle(new ChangePasswordCommand(current, NewPassword), cts.Token);

        Assert.Equal(outcome == "succeeded", result.IsSuccess);
        Assert.Contains("save", f.Timeline);
        Assert.Contains("commit", f.Timeline);
    }
}

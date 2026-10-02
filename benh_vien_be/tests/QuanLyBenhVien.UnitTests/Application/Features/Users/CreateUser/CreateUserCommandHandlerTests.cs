using Microsoft.Extensions.Time.Testing;
using QuanLyBenhVien.Application.Common.Auditing;
using QuanLyBenhVien.Application.Common.Identity;
using QuanLyBenhVien.Application.Common.Models;
using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Application.Features.Users.Common;
using QuanLyBenhVien.Application.Features.Users.CreateUser;
using QuanLyBenhVien.Application.Features.Users.ListUsers;
using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Features.Users.CreateUser;

internal sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId { get; set; } = Guid.NewGuid();
    public Guid? SessionFamilyId { get; set; }
    public int? SecurityVersion { get; set; }
}

internal sealed class FakeUserRepository : IUserRepository
{
    public User? Added { get; private set; }
    public Task<User?> GetByEmailAsync(string email, CancellationToken ct = default) => Task.FromResult<User?>(null);
    public Task<User> GetUserByIdAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<User?> GetForUpdateAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task AddUserAsync(User user, CancellationToken ct = default) { Added = user; return Task.CompletedTask; }
    public void UpdateUser(User user) { }
    public Task<bool> EmailExistsAsync(string email, CancellationToken ct = default) => Task.FromResult(false);
    public Task<User?> GetWithAccessAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<User?> GetWithAccessForUpdateAsync(Guid id, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken ct = default) => throw new NotSupportedException();
    public Task<int> CountActiveUsersInRoleAsync(Guid roleId, Guid? excludingUserId, CancellationToken ct = default) => Task.FromResult(0);
    public Task<IReadOnlyList<Guid>> GetUserIdsInRoleAsync(Guid roleId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public Task AcquireAdminSafetyLockAsync(CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeRoleRepository : IRoleRepository
{
    public Task<Role?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Role?>(null);
    public Task<Role?> GetByCodeAsync(string code, CancellationToken ct = default) => Task.FromResult<Role?>(null);
    public Task<IReadOnlyList<Role>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Role>>([]);
    public Task<bool> CodeExistsAsync(string code, CancellationToken ct = default) => Task.FromResult(false);
    public Task<Role?> GetForUpdateAsync(Guid id, CancellationToken ct = default) => Task.FromResult<Role?>(null);
    public Task<IReadOnlyList<Guid>> LockAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    public void MarkChanged(Role role) { }
    public Task AddAsync(Role role, CancellationToken ct = default) => Task.CompletedTask;
}

internal sealed class FakeReadService(FakeUserRepository users) : IUsersReadService
{
    public Task<PagedResult<UserSummaryDto>> ListAsync(ListUsersQuery query, CancellationToken ct) => throw new NotSupportedException();

    public Task<UserDetailDto?> GetAsync(Guid id, CancellationToken ct)
    {
        var u = users.Added!;
        return Task.FromResult<UserDetailDto?>(new UserDetailDto(u.Id, u.Email, u.FullName, u.IsActive, u.MustChangePassword, [], [], [], 1));
    }
}

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

internal sealed class FakePasswordHasher : IPasswordHasher
{
    public string Hash(string password) => "hash:" + password;
    public bool Verify(string password, string passwordHash) => passwordHash == "hash:" + password;
    public void SimulateVerify(string password) { }
}

/// Trả lần lượt các giá trị; giá trị cuối lặp lại mãi.
internal sealed class QueueGenerator(params string[] values) : IInitialPasswordGenerator
{
    private readonly Queue<string> _values = new(values);
    public int Calls { get; private set; }

    public string Generate()
    {
        Calls++;
        return _values.Count > 1 ? _values.Dequeue() : _values.Peek();
    }
}

internal sealed class FakeAuditWriter : IAuditWriter
{
    public List<string> Actions { get; } = [];

    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null) => Actions.Add(action);
}

public class CreateUserCommandHandlerTests

{
    private static (CreateUserCommandHandler Handler, FakeUserRepository Users, QueueGenerator Generator, FakeAuditWriter Audit) Create(params string[] passwords)
    {
        var users = new FakeUserRepository();
        var generator = new QueueGenerator(passwords);
        var audit = new FakeAuditWriter();
        var handler = new CreateUserCommandHandler(users, new FakeRoleRepository(), new FakeReadService(users), new FakeUnitOfWork(),
            new FakePasswordHasher(), generator, audit, new FakeCurrentUser(),
            new FakeTimeProvider(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));
        return (handler, users, generator, audit);
    }

    [Fact]
    public async Task RegeneratesWhenCandidateContainsEmailLocalPart_StoresOnlyHash_ReturnsPlaintextOnce()
    {
        var (handler, users, generator, audit) = Create("xxBacSiAnxxYYzz9", "Valid-Pass-2345A");

        var result = await handler.Handle(new CreateUserCommand("bacsian@test.local", "Bác sĩ An", []), default);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, generator.Calls);
        Assert.Equal("Valid-Pass-2345A", result.Value.InitialPassword);
        Assert.Equal("hash:Valid-Pass-2345A", users.Added!.PasswordHash);
        Assert.NotEqual(result.Value.InitialPassword, users.Added.PasswordHash);
        Assert.True(users.Added.MustChangePassword);
        Assert.Equal([AuditActions.UserCreate], audit.Actions);
    }

    [Fact]
    public async Task RegeneratesWhenTooShort()
    {
        var (handler, _, _, _) = Create("short", "Valid-Pass-2345A");

        var result = await handler.Handle(new CreateUserCommand("zq@test.local", "A", []), default);

        Assert.Equal("Valid-Pass-2345A", result.Value.InitialPassword);
    }

    [Fact]
    public async Task ThrowsWhenNoCandidateSatisfiesPolicy_AndAddsNothing()
    {
        var (handler, users, _, _) = Create("bacsian-always-bad");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.Handle(new CreateUserCommand("bacsian@test.local", "A", []), default));
        Assert.Null(users.Added);
    }
}

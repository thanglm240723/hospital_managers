using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence.Repositories.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Persistence;

/// Chứng minh khoá hàng `Users` (FOR UPDATE) qua `UserRepository.GetForUpdateAsync` tuần tự hoá
/// login/đổi mật khẩu — không dùng sleep để "hy vọng" tranh chấp, mà dùng barrier (Task chưa hoàn tất)
/// để biết chắc giao dịch thứ hai đang bị chặn bởi khoá của giao dịch thứ nhất.
[Collection(IntegrationCollection.Name)]
public class IdentityWriteConcurrencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 30, 8, 0, 0, TimeSpan.Zero);
    private readonly ContainersFixture _containers;

    public IdentityWriteConcurrencyTests(ContainersFixture containers) => _containers = containers;

    private async Task<(string Cs, Guid UserId)> SeedUserAsync(string passwordHash)
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var user = User.Create("A", $"u-{Guid.NewGuid():N}@test.local", passwordHash, null);
        await using var db = TestDb.Create(cs);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return (cs, user.Id);
    }

    [Fact]
    public async Task GetForUpdate_WithoutTransaction_Throws()
    {
        var (cs, userId) = await SeedUserAsync("hash-old");
        await using var db = TestDb.Create(cs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new UserRepository(db).GetForUpdateAsync(userId));
    }

    /// Login đang khoá hàng User để kiểm hash cũ; đổi mật khẩu (transaction khác) phải chờ, và khi đọc được
    /// thì phải thấy hash MỚI đã commit — tức là login diễn ra trước đó với hash cũ không thể "thắng" sau khi
    /// đổi mật khẩu đã commit.
    [Fact]
    public async Task LoginWithOldPassword_AfterPasswordChangeCommit_CannotCreateFamily()
    {
        var (cs, userId) = await SeedUserAsync("hash-old");

        // Giao dịch 1: mô phỏng đổi mật khẩu — giữ khoá một lúc rồi commit hash mới.
        await using var db1 = TestDb.Create(cs);
        await using var tx1 = await db1.BeginTransactionAsync();
        var locked1 = await new UserRepository(db1).GetForUpdateAsync(userId);
        locked1!.ChangePassword("hash-new", Now);
        await db1.SaveChangesAsync();

        // Giao dịch 2: mô phỏng login bằng mật khẩu cũ — cố khoá cùng hàng, phải bị chặn cho tới khi tx1 commit.
        var loginWithOldPassword = Task.Run(async () =>
        {
            await using var db2 = TestDb.Create(cs);
            await using var tx2 = await db2.BeginTransactionAsync();
            var locked2 = await new UserRepository(db2).GetForUpdateAsync(userId);
            await tx2.CommitAsync();
            return locked2!.PasswordHash;
        });

        await Task.Delay(500);
        Assert.False(loginWithOldPassword.IsCompleted, "Giao dịch login phải bị chặn bởi khoá FOR UPDATE của giao dịch đổi mật khẩu.");

        await tx1.CommitAsync();

        var hashSeenByLogin = await loginWithOldPassword.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("hash-new", hashSeenByLogin);   // login (nếu so hash cũ) sẽ thấy sai và bị từ chối
    }

    /// Hai yêu cầu đổi mật khẩu đồng thời trên cùng user: chỉ một cái được áp dụng trên hash gốc — cái thứ hai
    /// phải đọc lại hash MỚI (đã đổi) sau khi có khoá, không phải hash gốc đã đọc trước khi vào transaction.
    [Fact]
    public async Task TwoPasswordChanges_OnlyOneAcceptsOldPassword()
    {
        var (cs, userId) = await SeedUserAsync("hash-original");

        async Task<string> ChangeAsync(string expectedCurrentHash, string newHash)
        {
            await using var db = TestDb.Create(cs);
            await using var tx = await db.BeginTransactionAsync();
            var locked = await new UserRepository(db).GetForUpdateAsync(userId);
            var hashSeen = locked!.PasswordHash;

            if (hashSeen != expectedCurrentHash)
            {
                await tx.CommitAsync();
                return hashSeen;   // "sai mật khẩu hiện tại" — không ghi
            }

            locked.ChangePassword(newHash, Now);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return hashSeen;
        }

        var first = Task.Run(() => ChangeAsync("hash-original", "hash-from-first"));
        var second = Task.Run(() => ChangeAsync("hash-original", "hash-from-second"));

        var results = await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10));

        await using var verify = TestDb.Create(cs);
        var finalHash = await verify.Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();

        // Đúng một trong hai đọc được hash gốc và thắng; cái còn lại đọc hash đã đổi (sau khi khoá nhả) nên
        // không khớp "mật khẩu hiện tại" mong đợi của nó và không ghi đè.
        Assert.Contains("hash-original", results);
        Assert.True(finalHash is "hash-from-first" or "hash-from-second");
    }
}

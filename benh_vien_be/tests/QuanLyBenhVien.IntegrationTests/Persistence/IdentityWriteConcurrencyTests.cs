using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence.Repositories.Identity;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Persistence;

/// Chứng minh khoá hàng `Users` (FOR UPDATE) qua `UserRepository.GetForUpdateAsync` tuần tự hoá các giao dịch
/// đụng cùng một user. Không dùng sleep để "hy vọng" tranh chấp: chờ bằng cách polling `pg_stat_activity`
/// (wait_event_type = 'Lock') tới khi giao dịch thứ hai thật sự đang bị Postgres chặn, có timeout.
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

    /// Chờ tới khi có ít nhất một phiên Postgres đang bị chặn bởi một khoá hàng (wait_event_type = 'Lock').
    /// Poll ngắn có timeout — không phải sleep-rồi-hy-vọng: nếu điều kiện không xảy ra trong `timeout`,
    /// ném lỗi rõ ràng thay vì âm thầm cho qua.
    private async Task WaitUntilBlockedOnLockAsync(string connectionString, TimeSpan timeout)
    {
        using var deadline = new CancellationTokenSource(timeout);
        try
        {
            while (true)
            {
                await using var conn = new NpgsqlConnection(connectionString);
                await conn.OpenAsync(deadline.Token);
                await using var cmd = new NpgsqlCommand(
                    "SELECT count(*) FROM pg_stat_activity WHERE wait_event_type = 'Lock' AND datname = current_database()",
                    conn);
                var count = (long)(await cmd.ExecuteScalarAsync(deadline.Token))!;
                if (count > 0) return;

                await Task.Delay(TimeSpan.FromMilliseconds(25), deadline.Token);
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Không thấy phiên nào bị chặn bởi khoá hàng trong {timeout} — FOR UPDATE có thể đã không hoạt động.");
        }
    }

    [Fact]
    public async Task GetForUpdate_WithoutTransaction_Throws()
    {
        var (cs, userId) = await SeedUserAsync("hash-old");
        await using var db = TestDb.Create(cs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new UserRepository(db).GetForUpdateAsync(userId));
    }

    /// Giao dịch A khoá hàng User (FOR UPDATE) và giữ khoá; ta CHỜ CÓ BẰNG CHỨNG (không đoán) rằng giao dịch B
    /// đang thật sự bị Postgres chặn khi cùng gọi GetForUpdateAsync, rồi mới cho A commit. B chỉ đọc được sau
    /// khi A đã commit, nên B phải thấy hash MỚI — chứng minh FOR UPDATE tuần tự hoá đúng, không phải trùng hợp
    /// về thời gian.
    [Fact]
    public async Task GetForUpdateAsync_SecondTransactionBlocksUntilFirstCommits_ThenSeesLatestHash()
    {
        var (cs, userId) = await SeedUserAsync("hash-old");

        await using var db1 = TestDb.Create(cs);
        await using var tx1 = await db1.BeginTransactionAsync();
        var locked1 = await new UserRepository(db1).GetForUpdateAsync(userId);
        locked1!.ChangePassword("hash-new", Now);
        await db1.SaveChangesAsync();

        var second = Task.Run(async () =>
        {
            await using var db2 = TestDb.Create(cs);
            await using var tx2 = await db2.BeginTransactionAsync();
            var locked2 = await new UserRepository(db2).GetForUpdateAsync(userId);
            await tx2.CommitAsync();
            return locked2!.PasswordHash;
        });

        // Bằng chứng B đang bị chặn — không phải "chờ rồi đoán chưa xong".
        await WaitUntilBlockedOnLockAsync(cs, TimeSpan.FromSeconds(10));
        Assert.False(second.IsCompleted, "Giao dịch thứ hai phải còn đang bị chặn tại thời điểm quan sát được wait_event_type='Lock'.");

        await tx1.CommitAsync();

        var hashSeenBySecond = await second.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("hash-new", hashSeenBySecond);
    }

    /// Hai giao dịch đổi mật khẩu đồng thời trên cùng user, cùng kỳ vọng "mật khẩu hiện tại" là hash gốc.
    /// Nếu KHÔNG có FOR UPDATE, cả hai đều có thể đọc hash gốc trước khi bên kia ghi (race) và cả hai đều ghi
    /// (last-writer-wins) — bài test cũ không phát hiện được vì assertion quá lỏng. Bài test này buộc thứ tự
    /// tất định bằng barrier chờ B thật sự bị chặn trước khi A commit, rồi khẳng định CHÍNH XÁC: A đọc hash gốc
    /// và thắng; B (đọc sau khi khoá được nhả) phải thấy hash MỚI của A — tức bị từ chối vì "sai mật khẩu hiện
    /// tại" — và hash cuối cùng trong DB là hash của B (người ghi sau).
    [Fact]
    public async Task TwoPasswordChanges_SecondObservesFirstsWriteAndIsRejected_LastWriterIsSecond()
    {
        var (cs, userId) = await SeedUserAsync("hash-original");

        async Task<(string HashSeen, bool Applied)> ChangeAsync(string expectedCurrentHash, string newHash, Func<Task>? afterLock = null)
        {
            await using var db = TestDb.Create(cs);
            await using var tx = await db.BeginTransactionAsync();
            var locked = await new UserRepository(db).GetForUpdateAsync(userId);
            var hashSeen = locked!.PasswordHash;

            if (afterLock is not null) await afterLock();

            if (hashSeen != expectedCurrentHash)
            {
                await tx.CommitAsync();
                return (hashSeen, false);   // "sai mật khẩu hiện tại" — không ghi
            }

            locked.ChangePassword(newHash, Now);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            return (hashSeen, true);
        }

        // A giữ khoá lại (chờ tín hiệu) và báo hiệu ngay khi đã khoá được hàng — B chỉ bắt đầu SAU khi biết chắc
        // A đã giữ khoá, để loại bỏ hoàn toàn khả năng B (chạy trên Task khác) tình cờ khoá được hàng trước A.
        // Nhờ vậy thứ tự tất định: A luôn đọc hash gốc và thắng, B luôn đọc sau A (sau khi khoá được nhả).
        var firstHasLockedRow = new TaskCompletionSource();
        var firstMayProceed = new TaskCompletionSource();
        var first = Task.Run(() => ChangeAsync("hash-original", "hash-from-first", async () =>
        {
            firstHasLockedRow.TrySetResult();
            await firstMayProceed.Task;
        }));

        await firstHasLockedRow.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var second = Task.Run(() => ChangeAsync("hash-original", "hash-from-second"));

        await WaitUntilBlockedOnLockAsync(cs, TimeSpan.FromSeconds(10));
        Assert.False(second.IsCompleted, "Giao dịch thứ hai phải còn đang bị chặn bởi khoá của giao dịch thứ nhất.");

        firstMayProceed.SetResult();
        var (firstHashSeen, firstApplied) = await first.WaitAsync(TimeSpan.FromSeconds(10));
        var (secondHashSeen, secondApplied) = await second.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal("hash-original", firstHashSeen);
        Assert.True(firstApplied);
        Assert.Equal("hash-from-first", secondHashSeen);   // B đọc SAU khi khoá được nhả -> thấy hash MỚI của A
        Assert.False(secondApplied);                       // -> không khớp "mật khẩu hiện tại" của B -> bị từ chối

        await using var verify = TestDb.Create(cs);
        var finalHash = await verify.Users.Where(u => u.Id == userId).Select(u => u.PasswordHash).SingleAsync();
        Assert.Equal("hash-from-first", finalHash);   // chỉ A ghi; B không ghi vì bị từ chối
    }
}

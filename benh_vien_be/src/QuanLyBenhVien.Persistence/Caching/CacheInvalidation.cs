namespace QuanLyBenhVien.Persistence.Caching;

/// Lệnh xoá key Redis chờ thực hiện. Ghi cùng transaction nghiệp vụ nên không bao giờ thất lạc.
public sealed class CacheInvalidation
{
    public Guid Id { get; private set; }
    public string Key { get; private set; } = default!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    /// Lease: chủ claim hiện tại và hạn lease. Null = chưa ai nhận; hết hạn = worker khác được nhận lại.
    public Guid? ClaimId { get; private set; }
    public DateTimeOffset? ClaimedUntilUtc { get; private set; }

    private CacheInvalidation() { }

    public static CacheInvalidation Create(string key, DateTimeOffset now)
        => new() { Id = Guid.CreateVersion7(), Key = key, CreatedAtUtc = now };

}

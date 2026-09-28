namespace QuanLyBenhVien.Persistence.Caching;

/// Lệnh xoá key Redis chờ thực hiện. Ghi cùng transaction nghiệp vụ nên không bao giờ thất lạc.
public sealed class CacheInvalidation
{
    private const int MaxErrorLength = 1000;

    public Guid Id { get; private set; }
    public string Key { get; private set; } = default!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public string? LastError { get; private set; }

    private CacheInvalidation() { }

    public static CacheInvalidation Create(string key, DateTimeOffset now)
        => new() { Id = Guid.CreateVersion7(), Key = key, CreatedAtUtc = now };

    public void MarkFailed(string error)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
    }
}

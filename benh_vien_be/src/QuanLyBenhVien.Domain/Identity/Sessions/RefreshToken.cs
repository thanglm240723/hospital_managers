using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Sessions;

/// Chỉ lưu hash SHA-256 của refresh token, không bao giờ lưu token gốc.
public sealed class RefreshToken : Entity<Guid>
{
    public Guid FamilyId { get; private set; }
    public string TokenHash { get; private set; } = default!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ConsumedAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReplacedById { get; private set; }

    public bool IsUsable => ConsumedAtUtc is null && RevokedAtUtc is null;

    private RefreshToken() { }

    internal static RefreshToken Issue(Guid familyId, string tokenHash, DateTimeOffset now, DateTimeOffset expiresAtUtc)
        => new()
        {
            Id = Guid.CreateVersion7(),
            FamilyId = familyId,
            TokenHash = tokenHash,
            CreatedAtUtc = now,
            ExpiresAtUtc = expiresAtUtc
        };

    internal void MarkConsumed(DateTimeOffset now, Guid replacedById)
    {
        ConsumedAtUtc = now;
        ReplacedById = replacedById;
    }

    internal void MarkRevoked(DateTimeOffset now)
    {
        if (IsUsable) RevokedAtUtc = now;
    }
}

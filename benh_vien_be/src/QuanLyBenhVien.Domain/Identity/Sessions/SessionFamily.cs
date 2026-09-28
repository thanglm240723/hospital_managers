using QuanLyBenhVien.Domain.Common;

namespace QuanLyBenhVien.Domain.Identity.Sessions;

/// Một lần đăng nhập = một family. Mọi refresh token xoay vòng trong family đều chung hạn tuyệt đối.
public sealed class SessionFamily : AggregateRoot<Guid>
{
    public static readonly TimeSpan AbsoluteLifetime = TimeSpan.FromDays(7);

    private readonly List<RefreshToken> _tokens = new();

    public Guid UserId { get; private set; }
    public SessionStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset AbsoluteExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public SessionRevokeReason? RevokeReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset? LastRefreshedAtUtc { get; private set; }
    /// Chỉ chứa các token repository đã nạp (token được trình + token còn dùng được), không phải toàn bộ lịch sử.
    public IReadOnlyCollection<RefreshToken> Tokens => _tokens.AsReadOnly();

    private SessionFamily() { }

    public static SessionFamily Start(Guid userId, string tokenHash, DateTimeOffset now, string? ipAddress, string? userAgent)
    {
        var family = new SessionFamily
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Status = SessionStatus.Active,
            CreatedAtUtc = now,
            AbsoluteExpiresAtUtc = now + AbsoluteLifetime,
            IpAddress = ipAddress,
            UserAgent = userAgent
        };
        family._tokens.Add(RefreshToken.Issue(family.Id, tokenHash, now, family.AbsoluteExpiresAtUtc));
        return family;
    }

    public bool IsActiveAt(DateTimeOffset now) => Status == SessionStatus.Active && now < AbsoluteExpiresAtUtc;

    /// Strict reuse: token đã dùng/đã thu hồi mà được trình lại ⇒ thu hồi cả family.
    public RotationResult Rotate(string presentedTokenHash, string newTokenHash, DateTimeOffset now)
    {
        if (Status != SessionStatus.Active) return RotationResult.NotActive;
        if (now >= AbsoluteExpiresAtUtc) return RotationResult.Expired;

        var presented = _tokens.SingleOrDefault(t => t.TokenHash == presentedTokenHash)
            ?? throw new InvalidOperationException("The presented refresh token was not loaded into this family.");

        if (!presented.IsUsable)
        {
            Revoke(SessionRevokeReason.Reuse, now);
            return RotationResult.ReuseDetected;
        }

        var next = RefreshToken.Issue(Id, newTokenHash, now, AbsoluteExpiresAtUtc);
        presented.MarkConsumed(now, next.Id);
        _tokens.Add(next);
        LastRefreshedAtUtc = now;
        return RotationResult.Rotated;
    }

    public void Revoke(SessionRevokeReason reason, DateTimeOffset now)
    {
        if (Status == SessionStatus.Revoked) return;
        Status = SessionStatus.Revoked;
        RevokedAtUtc = now;
        RevokeReason = reason;
        foreach (var token in _tokens) token.MarkRevoked(now);
    }
}

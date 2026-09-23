using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Sessions;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class SessionFamilyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    private static SessionFamily NewFamily() => SessionFamily.Start(Guid.NewGuid(), "h1", T0, "10.0.0.1", "UA");

    [Fact]
    public void Start_CreatesActiveFamilyWithOneTokenAndSevenDayLimit()
    {
        var family = NewFamily();

        Assert.Equal(SessionStatus.Active, family.Status);
        Assert.Equal(T0.AddDays(7), family.AbsoluteExpiresAtUtc);
        var token = Assert.Single(family.Tokens);
        Assert.Equal("h1", token.TokenHash);
        Assert.Equal(family.AbsoluteExpiresAtUtc, token.ExpiresAtUtc);
        Assert.True(token.IsUsable);
    }

    [Fact]
    public void Rotate_ConsumesOldTokenAndIssuesNewOneWithoutExtendingFamily()
    {
        var family = NewFamily();
        var later = T0.AddMinutes(14);

        var result = family.Rotate("h1", "h2", later);

        Assert.Equal(RotationResult.Rotated, result);
        var old = family.Tokens.Single(t => t.TokenHash == "h1");
        var next = family.Tokens.Single(t => t.TokenHash == "h2");
        Assert.Equal(later, old.ConsumedAtUtc);
        Assert.Equal(next.Id, old.ReplacedById);
        Assert.Equal(family.AbsoluteExpiresAtUtc, next.ExpiresAtUtc);
        Assert.Equal(later, family.LastRefreshedAtUtc);
    }

    [Fact]
    public void Rotate_ConsumedTokenPresentedAgain_RevokesWholeFamily()
    {
        var family = NewFamily();
        family.Rotate("h1", "h2", T0.AddMinutes(1));

        var result = family.Rotate("h1", "h3", T0.AddMinutes(2));

        Assert.Equal(RotationResult.ReuseDetected, result);
        Assert.Equal(SessionStatus.Revoked, family.Status);
        Assert.Equal(SessionRevokeReason.Reuse, family.RevokeReason);
        Assert.False(family.Tokens.Single(t => t.TokenHash == "h2").IsUsable);
        Assert.DoesNotContain(family.Tokens, t => t.TokenHash == "h3");
    }

    [Fact]
    public void Rotate_AfterAbsoluteExpiry_ReturnsExpired()
    {
        var family = NewFamily();

        Assert.Equal(RotationResult.Expired, family.Rotate("h1", "h2", T0.AddDays(7)));
    }

    [Fact]
    public void Rotate_RevokedFamily_ReturnsNotActive()
    {
        var family = NewFamily();
        family.Revoke(SessionRevokeReason.Logout, T0.AddMinutes(1));

        Assert.Equal(RotationResult.NotActive, family.Rotate("h1", "h2", T0.AddMinutes(2)));
    }

    [Fact]
    public void Revoke_IsIdempotentAndKeepsFirstReason()
    {
        var family = NewFamily();

        family.Revoke(SessionRevokeReason.Logout, T0.AddMinutes(1));
        family.Revoke(SessionRevokeReason.LogoutAll, T0.AddMinutes(2));

        Assert.Equal(SessionRevokeReason.Logout, family.RevokeReason);
        Assert.Equal(T0.AddMinutes(1), family.RevokedAtUtc);
        Assert.False(Assert.Single(family.Tokens).IsUsable);
        Assert.False(family.IsActiveAt(T0.AddMinutes(3)));
    }

    [Fact]
    public void SessionFamily_IsNotAuditable()
        => Assert.False(typeof(IAuditable).IsAssignableFrom(typeof(SessionFamily)));
}

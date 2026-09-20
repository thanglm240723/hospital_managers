using CleanArchCqrs.Infrastructure.Security;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_RoundTripSucceeds()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("P@ssw0rd123");

        Assert.True(hasher.Verify("P@ssw0rd123", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("P@ssw0rd123");

        Assert.False(hasher.Verify("wrong-password", hash));
    }
}

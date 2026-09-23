using CleanArchCqrs.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class CsrfTokenServiceTests
{
    private static CsrfTokenService Create(string key = "csrf-key-0123456789-0123456789-0123")
        => new(Options.Create(new AuthOptions { CsrfKey = key }));

    [Fact]
    public void TokenIsBoundToItsSessionFamily()
    {
        var service = Create();
        var familyId = Guid.NewGuid();

        var token = service.Create(familyId);

        Assert.True(service.IsValid(familyId, token));
        Assert.False(service.IsValid(Guid.NewGuid(), token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void InvalidTokens_AreRejected(string? token)
        => Assert.False(Create().IsValid(Guid.NewGuid(), token));

    [Fact]
    public void DifferentKey_ProducesDifferentToken()
    {
        var familyId = Guid.NewGuid();

        Assert.False(Create("another-key-0123456789-0123456789-01").IsValid(familyId, Create().Create(familyId)));
    }

    [Fact]
    public void ShortKey_Throws()
        => Assert.Throws<InvalidOperationException>(() => Create("short"));
}

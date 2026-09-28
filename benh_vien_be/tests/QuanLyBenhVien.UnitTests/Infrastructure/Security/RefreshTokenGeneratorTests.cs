using QuanLyBenhVien.Infrastructure.Security;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Infrastructure.Security;

public class RefreshTokenGeneratorTests
{
    private readonly RefreshTokenGenerator _generator = new();

    [Fact]
    public void Generate_Returns256BitBase64UrlTokenAndItsHash()
    {
        var generated = _generator.Generate();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", generated.Token);
        Assert.Matches("^[0-9a-f]{64}$", generated.Hash);
        Assert.Equal(_generator.Hash(generated.Token), generated.Hash);
    }

    [Fact]
    public void Generate_ProducesDifferentTokensEachTime()
        => Assert.NotEqual(_generator.Generate().Token, _generator.Generate().Token);
}

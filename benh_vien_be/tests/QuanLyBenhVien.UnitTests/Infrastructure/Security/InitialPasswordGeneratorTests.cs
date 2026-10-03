using QuanLyBenhVien.Application.Common.Security;
using QuanLyBenhVien.Infrastructure.Security;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Infrastructure.Security;

public class InitialPasswordGeneratorTests
{
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    [Fact]
    public void Generates16CharsFromReadableAlphabet_SatisfyingPolicy_AndDistinct()
    {
        var generator = new InitialPasswordGenerator();
        var values = Enumerable.Range(0, 200).Select(_ => generator.Generate()).ToList();

        Assert.All(values, v =>
        {
            Assert.Equal(16, v.Length);
            Assert.All(v, c => Assert.Contains(c, Alphabet));
            Assert.InRange(v.Length, PasswordPolicy.MinLength, PasswordPolicy.MaxLength);
        });
        Assert.Equal(values.Count, values.Distinct().Count());
    }
}

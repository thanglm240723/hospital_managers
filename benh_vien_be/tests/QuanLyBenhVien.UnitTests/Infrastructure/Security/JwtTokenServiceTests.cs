using QuanLyBenhVien.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Infrastructure.Security;

public class JwtTokenServiceTests
{
    [Fact]
    public void Issue_CarriesOnlyIdentityClaims()
    {
        var now = new DateTimeOffset(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
        var time = new FakeTimeProvider(now);
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('k', 32),
        }), time);
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        var token = service.Issue(userId, familyId, securityVersion: 3);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Value);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(userId.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal(familyId.ToString(), jwt.GetClaim("fid").Value);
        Assert.Equal("3", jwt.GetClaim("sv").Value);
        Assert.Equal(
            new[] { "aud", "exp", "fid", "iat", "iss", "jti", "nbf", "sub", "sv" },
            jwt.Claims.Select(c => c.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(now.AddMinutes(15), token.ExpiresAtUtc);
    }
}

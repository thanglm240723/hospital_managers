using QuanLyBenhVien.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Infrastructure.Security;

public class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_CarriesOnlyIdentityClaims()
    {
        var service = new JwtTokenService(Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('k', 32),
        }));
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();

        var token = service.CreateAccessToken(userId, familyId, securityVersion: 3);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);
        Assert.Equal("HS256", jwt.Alg);
        Assert.Equal(userId.ToString(), jwt.GetClaim("sub").Value);
        Assert.Equal(familyId.ToString(), jwt.GetClaim("fid").Value);
        Assert.Equal("3", jwt.GetClaim("sv").Value);
        Assert.Equal(
            new[] { "aud", "exp", "fid", "iat", "iss", "jti", "nbf", "sub", "sv" },
            jwt.Claims.Select(c => c.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal));
        Assert.InRange(token.ExpiresAtUtc, DateTimeOffset.UtcNow.AddMinutes(14), DateTimeOffset.UtcNow.AddMinutes(15).AddSeconds(5));
    }
}

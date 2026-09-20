using CleanArchCqrs.Domain.Constants;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_IncludesExpectedClaimsAndExpiry()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('k', 32),
            AccessTokenMinutes = 60
        });
        var service = new JwtTokenService(options);
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);

        var token = service.CreateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);
        Assert.Equal(user.Id.ToString(), jwt.GetClaim(JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, jwt.GetClaim("email").Value);
        Assert.Equal(user.FullName, jwt.GetClaim("name").Value);
        Assert.Equal(Roles.Admin, jwt.GetClaim("role").Value);
        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.True(token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(59));
    }
}

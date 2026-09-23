using System.Security.Claims;
using CleanArchCqrs.API.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Services;

public class CurrentUserTests
{
    [Fact]
    public void ReadsSubAndFidClaims()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim("fid", familyId.ToString())], "Bearer"))
        };

        var current = new CurrentUser(new HttpContextAccessor { HttpContext = http });

        Assert.Equal(userId, current.UserId);
        Assert.Equal(familyId, current.SessionFamilyId);
        Assert.True(current.IsAuthenticated);
    }

    [Fact]
    public void Anonymous_ReturnsNulls()
    {
        var current = new CurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.Null(current.UserId);
        Assert.Null(current.SessionFamilyId);
        Assert.False(current.IsAuthenticated);
    }
}

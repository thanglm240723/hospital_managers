using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class UserAuditableTests
{
    [Fact]
    public void User_ImplementsIAuditable()
    {
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(User)));
    }
}

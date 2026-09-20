// tests/CleanArchCqrs.UnitTests/Domain/Identity/UserLoginHistoryTests.cs
using CleanArchCqrs.Domain.Identity;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class UserLoginHistoryTests
{
    [Fact]
    public void Failed_SetsSuccessFalseAndFailureReason()
    {
        var userId = Guid.NewGuid();

        var record = UserLoginHistory.Failed(userId, "a@example.com", "InvalidPassword", "127.0.0.1", "curl/8.0");

        Assert.False(record.Success);
        Assert.Equal("InvalidPassword", record.FailureReason);
        Assert.Equal(userId, record.UserId);
        Assert.Equal("a@example.com", record.EmailAttempted);
    }

    [Fact]
    public void Failed_WithNullUserId_AllowsUnknownEmail()
    {
        var record = UserLoginHistory.Failed(null, "unknown@example.com", "EmailNotFound", null, null);

        Assert.Null(record.UserId);
        Assert.Equal("EmailNotFound", record.FailureReason);
    }

    [Fact]
    public void Succeeded_SetsSuccessTrueAndNoFailureReason()
    {
        var userId = Guid.NewGuid();

        var record = UserLoginHistory.Succeeded(userId, "a@example.com", "127.0.0.1", "curl/8.0");

        Assert.True(record.Success);
        Assert.Null(record.FailureReason);
        Assert.Equal(userId, record.UserId);
    }
}

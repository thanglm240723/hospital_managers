using QuanLyBenhVien.Domain.Common.Auditing;
using QuanLyBenhVien.Domain.Identity;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Domain.Identity;

public class UserAuditableTests
{
    [Fact]
    public void User_ImplementsIAuditable()
    {
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(User)));
    }
}

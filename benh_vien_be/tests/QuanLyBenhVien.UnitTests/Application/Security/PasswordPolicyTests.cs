using QuanLyBenhVien.Application.Common.Security;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Application.Security;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("nguyenvana-2026!", "NguyenVanA@benhvien.vn", true)]
    [InlineData("Xyz-1234567", "nguyenvana@benhvien.vn", false)]
    public void ContainsEmailLocalPart_IsCaseInsensitive(string password, string email, bool expected)
        => Assert.Equal(expected, PasswordPolicy.ContainsEmailLocalPart(password, email));
}

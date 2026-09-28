using System.Net;
using QuanLyBenhVien.IntegrationTests.Infrastructure;

namespace QuanLyBenhVien.IntegrationTests.Helpers;

public static class AdminSession
{
    /// Admin seed của factory. Lần đầu: đăng nhập bằng mật khẩu tạm rồi đổi sang AdminPassword.
    public static async Task<AuthTestClient> LoginAsAdminAsync(this ApiFactory factory)
    {
        var client = new AuthTestClient(factory.CreateHttpsClient());
        if ((await client.LoginAsync(factory.AdminEmail, TestConstants.AdminPassword)).StatusCode == HttpStatusCode.OK)
            return client;

        (await client.LoginAsync(factory.AdminEmail, TestConstants.AdminTempPassword)).EnsureSuccessStatusCode();
        (await client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = TestConstants.AdminTempPassword, newPassword = TestConstants.AdminPassword }))
            .EnsureSuccessStatusCode();
        return client;
    }
}

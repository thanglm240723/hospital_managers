using System.Net;
using QuanLyBenhVien.IntegrationTests.Helpers;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Xunit;

namespace QuanLyBenhVien.IntegrationTests.Auth;

/// Sau đổi mật khẩu: family hiện tại refresh được, family khác bị thu hồi (refresh 401).
[Collection(IntegrationCollection.Name)]
public class PasswordChangeRefreshTests : IAsyncLifetime
{
    private const string NewPassword = "Brand-New-Pass-99";
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public PasswordChangeRefreshTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task AfterChange_CurrentFamilyCanRefresh_OtherFamilyCannot()
    {
        var email = TestData.NewEmail("staff");
        await TestData.CreateUserAsync(_factory, email);
        var current = new AuthTestClient(_factory.CreateHttpsClient());
        (await current.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        var other = new AuthTestClient(_factory.CreateHttpsClient());
        (await other.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        (await current.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = TestData.DefaultPassword, newPassword = NewPassword })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await other.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.RefreshAsync()).StatusCode);
    }
}

using QuanLyBenhVien.Application.Common.Interfaces;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.Caching;
using QuanLyBenhVien.Infrastructure.Persistence;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace QuanLyBenhVien.IntegrationTests.Helpers;

public static class TestData
{
    public const string DefaultPassword = "Test-Password-123";

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@test.local";

    /// Tạo user thẳng vào DB (không qua API). mustChangePassword=false giả lập user đã đổi mật khẩu lần đầu.
    public static async Task<Guid> CreateUserAsync(ApiFactory factory, string email, string password = DefaultPassword,
        bool mustChangePassword = false, bool isActive = true, params string[] roleCodes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = User.Create("Test User", email, hasher.Hash(password), null);
        if (!mustChangePassword) user.ChangePassword(hasher.Hash(password));
        if (roleCodes.Length > 0)
        {
            var roleIds = await db.Roles.Where(r => roleCodes.Contains(r.Code)).Select(r => r.Id).ToListAsync();
            user.SetRoles(roleIds, null, DateTimeOffset.UtcNow);
        }
        if (!isActive) user.Deactivate();

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    /// Cấp quyền lẻ thẳng trong DB và xoá cache quyền — dùng để dựng tình huống test, không phải để test API cấp quyền.
    public static async Task GrantAsync(ApiFactory factory, Guid userId, params string[] permissionCodes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.Include(u => u.PermissionGrants).SingleAsync(u => u.Id == userId);
        foreach (var code in permissionCodes)
            user.GrantPermission(code, "test setup", null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
            .KeyDeleteAsync(CacheKeys.Permissions(userId));
    }

    public static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

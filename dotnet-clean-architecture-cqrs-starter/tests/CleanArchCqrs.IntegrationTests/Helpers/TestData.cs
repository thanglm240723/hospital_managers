using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.IntegrationTests.Helpers;

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

    public static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}

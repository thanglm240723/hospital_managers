using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Authorization;

[Collection(IntegrationCollection.Name)]
public class PermissionServiceTests
{
    private readonly ContainersFixture _containers;

    public PermissionServiceTests(ContainersFixture containers) => _containers = containers;

    private static async Task<UserAccess> GetAsync(ApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId);
    }

    [Fact]
    public async Task FirstCall_LoadsFromDbAndCachesWithoutTtl()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);

        var access = await GetAsync(factory, userId);

        Assert.Contains(Permissions.Users.Read, access.Permissions);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        using var cached = JsonDocument.Parse((string)(await redis.StringGetAsync(CacheKeys.Permissions(userId)))!);
        Assert.Contains(cached.RootElement.GetProperty("permissions").EnumerateArray(), p => p.GetString() == Permissions.Users.Read);
        Assert.Null(await redis.KeyTimeToLiveAsync(CacheKeys.Permissions(userId)));
    }

    [Fact]
    public async Task CachedValue_IsUsedUntilInvalidated()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);
        await GetAsync(factory, userId);   // làm ấm cache

        await TestData.QueryAsync(factory, async db =>
        {
            await db.Set<UserRole>().Where(r => r.UserId == userId).ExecuteDeleteAsync();   // đổi DB "lén", không invalidate
            return 0;
        });
        Assert.Contains(Permissions.Users.Read, (await GetAsync(factory, userId)).Permissions);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
            invalidator.InvalidatePermissions(userId);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
            await invalidator.FlushAsync();
        }
        Assert.DoesNotContain(Permissions.Users.Read, (await GetAsync(factory, userId)).Permissions);
    }

    [Fact]
    public async Task RedisDown_FallsBackToDatabase()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);

        Assert.Contains(Permissions.Users.Read, (await GetAsync(factory, userId)).Permissions);
    }

    [Fact]
    public async Task InactiveUser_HasNoPermissions_UnknownUser_HasNone()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var inactive = await TestData.CreateUserAsync(factory, TestData.NewEmail(), isActive: false, roleCodes: [SystemRoles.Admin]);

        Assert.Empty((await GetAsync(factory, inactive)).Permissions);
        Assert.Empty((await GetAsync(factory, Guid.NewGuid())).Permissions);
    }

    [Fact]
    public async Task GrantedPermission_IsPartOfEffectiveSet()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail());
        await TestData.GrantAsync(factory, userId, Permissions.Catalog.Read);

        Assert.Equal(new[] { Permissions.Catalog.Read }, (await GetAsync(factory, userId)).Permissions);
    }
}

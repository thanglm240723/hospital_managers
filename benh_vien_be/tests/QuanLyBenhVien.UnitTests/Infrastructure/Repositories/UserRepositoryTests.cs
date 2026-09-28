using QuanLyBenhVien.Domain.Exceptions;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Infrastructure.Persistence;
using QuanLyBenhVien.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Infrastructure.Repositories;

public class UserRepositoryTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public async Task GetByEmailAsync_NormalizesEmailBeforeLookup()
    {
        using var context = CreateContext();
        var user = User.Create("Nguyen Van A", "MixedCase@Example.com", "hash", null);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var repo = new UserRepository(context);

        var found = await repo.GetByEmailAsync("mixedcase@example.com");

        Assert.NotNull(found);
        Assert.Equal(user.Id, found!.Id);
    }

    [Fact]
    public async Task GetUserByIdAsync_MissingUser_ThrowsNotFoundException()
    {
        using var context = CreateContext();
        var repo = new UserRepository(context);

        await Assert.ThrowsAsync<NotFoundException>(() => repo.GetUserByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task EmailExistsAsync_NormalizesEmailBeforeLookup()
    {
        using var context = CreateContext();
        context.Users.Add(User.Create("Nguyen Van A", "exists@example.com", "hash", null));
        await context.SaveChangesAsync();
        var repo = new UserRepository(context);

        Assert.True(await repo.EmailExistsAsync("EXISTS@example.com"));
        Assert.False(await repo.EmailExistsAsync("nope@example.com"));
    }

    [Fact]
    public async Task AddUserAsync_ThenSaveChanges_PersistsUser()
    {
        using var context = CreateContext();
        var repo = new UserRepository(context);
        var user = User.Create("Nguyen Van A", "new@example.com", "hash", null);

        await repo.AddUserAsync(user);
        await context.SaveChangesAsync();

        Assert.Equal(1, context.Users.Count());
    }
}

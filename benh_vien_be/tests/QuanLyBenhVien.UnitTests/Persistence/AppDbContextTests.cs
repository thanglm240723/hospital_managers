using QuanLyBenhVien.Domain.Common;
using QuanLyBenhVien.Domain.Identity;
using QuanLyBenhVien.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace QuanLyBenhVien.UnitTests.Persistence;

public class AppDbContextTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public void AppDbContext_ImplementsIUnitOfWork()
    {
        using var context = CreateContext();
        Assert.IsAssignableFrom<IUnitOfWork>(context);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsAddedUser()
    {
        using var context = CreateContext();
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null);

        context.Users.Add(user);
        var affected = await context.SaveChangesAsync();

        Assert.Equal(1, affected);
        Assert.Single(context.Users);
    }
}

using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Repositories;

public class UserLoginHistoryRepositoryTests
{
    [Fact]
    public async Task AddAsync_PersistsRecordOnSaveChanges()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var repo = new UserLoginHistoryRepository(context);
        var record = UserLoginHistory.Failed(null, "unknown@example.com", "EmailNotFound", "127.0.0.1", "test-agent");

        await repo.AddAsync(record);
        await context.SaveChangesAsync();

        Assert.Single(context.UserLoginHistories);
    }
}

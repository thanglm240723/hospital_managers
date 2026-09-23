using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Persistence;

[Collection(IntegrationCollection.Name)]
public class SessionRepositoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);
    private readonly ContainersFixture _containers;

    public SessionRepositoryTests(ContainersFixture containers) => _containers = containers;

    private async Task<(string Cs, User User, SessionFamily Family)> SeedFamilyAsync(params string[] rotations)
    {
        var cs = await TestDb.CreateMigratedDatabaseAsync(_containers);
        var user = User.Create("A", $"u-{Guid.NewGuid():N}@test.local", "hash", null);
        var family = SessionFamily.Start(user.Id, "h1", Now, null, null);
        var current = "h1";
        foreach (var next in rotations)
        {
            family.Rotate(current, next, Now.AddMinutes(1));
            current = next;
        }

        await using var db = TestDb.Create(cs);
        db.Users.Add(user);
        db.SessionFamilies.Add(family);
        await db.SaveChangesAsync();
        return (cs, user, family);
    }

    [Fact]
    public async Task FindFamilyIdByTokenHash_ReturnsFamilyOrNull()
    {
        var (cs, _, family) = await SeedFamilyAsync();
        await using var db = TestDb.Create(cs);
        var repo = new SessionRepository(db);

        Assert.Equal(family.Id, await repo.FindFamilyIdByTokenHashAsync("h1"));
        Assert.Null(await repo.FindFamilyIdByTokenHashAsync("missing"));
    }

    [Fact]
    public async Task GetForUpdate_WithoutTransaction_Throws()
    {
        var (cs, _, family) = await SeedFamilyAsync();
        await using var db = TestDb.Create(cs);

        await Assert.ThrowsAsync<InvalidOperationException>(() => new SessionRepository(db).GetForUpdateAsync(family.Id, "h1"));
    }

    [Fact]
    public async Task GetForUpdate_LoadsPresentedAndUsableTokensOnly()
    {
        var (cs, _, family) = await SeedFamilyAsync("h2", "h3");   // h1, h2 consumed; h3 usable
        await using var db = TestDb.Create(cs);
        await using var tx = await db.BeginTransactionAsync();

        var loaded = await new SessionRepository(db).GetForUpdateAsync(family.Id, "h1");

        Assert.Equal(new[] { "h1", "h3" }, loaded!.Tokens.Select(t => t.TokenHash).OrderBy(h => h));
    }

    [Fact]
    public async Task GetForUpdate_SecondTransactionWaitsAndThenSeesConsumedToken()
    {
        var (cs, _, family) = await SeedFamilyAsync();
        await using var db1 = TestDb.Create(cs);
        await using var tx1 = await db1.BeginTransactionAsync();
        var first = await new SessionRepository(db1).GetForUpdateAsync(family.Id, "h1");

        var second = Task.Run(async () =>
        {
            await using var db2 = TestDb.Create(cs);
            await using var tx2 = await db2.BeginTransactionAsync();
            var loaded = await new SessionRepository(db2).GetForUpdateAsync(family.Id, "h1");
            var result = loaded!.Rotate("h1", "h-second", Now.AddMinutes(2));
            await db2.SaveChangesAsync();
            await tx2.CommitAsync();
            return result;
        });

        await Task.Delay(500);
        Assert.False(second.IsCompleted);   // bị chặn bởi khoá của transaction 1

        first!.Rotate("h1", "h-first", Now.AddMinutes(1));
        await db1.SaveChangesAsync();
        await tx1.CommitAsync();

        Assert.Equal(RotationResult.ReuseDetected, await second);
    }

    [Fact]
    public async Task GetActiveByUserForUpdate_ReturnsOnlyActiveFamilies()
    {
        var (cs, user, family) = await SeedFamilyAsync();
        var revoked = SessionFamily.Start(user.Id, "other-h1", Now, null, null);
        revoked.Revoke(SessionRevokeReason.Logout, Now);
        await using (var seed = TestDb.Create(cs))
        {
            seed.SessionFamilies.Add(revoked);
            await seed.SaveChangesAsync();
        }

        await using var db = TestDb.Create(cs);
        await using var tx = await db.BeginTransactionAsync();
        var families = await new SessionRepository(db).GetActiveByUserForUpdateAsync(user.Id);

        Assert.Equal(family.Id, Assert.Single(families).Id);
    }
}

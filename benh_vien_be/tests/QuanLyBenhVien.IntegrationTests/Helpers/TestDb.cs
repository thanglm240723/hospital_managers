using QuanLyBenhVien.Infrastructure.Persistence;
using QuanLyBenhVien.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace QuanLyBenhVien.IntegrationTests.Helpers;

public static class TestDb
{
    public static async Task<string> CreateMigratedDatabaseAsync(ContainersFixture containers)
    {
        var connectionString = await containers.CreateDatabaseAsync();
        await using var db = Create(connectionString);
        await db.Database.MigrateAsync();
        return connectionString;
    }

    public static AppDbContext Create(string connectionString)
        => new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options);
}

using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Infrastructure;

/// Một Postgres + một Redis dùng chung cho cả collection. Mỗi ApiFactory tạo database riêng
/// để các test class không giẫm dữ liệu của nhau.
public sealed class ContainersFixture : IAsyncLifetime
{
    // max_connections mặc định (100) không đủ khi nhiều ApiFactory (mỗi class một database + một Npgsql pool
    // riêng) chạy tuần tự trong cùng collection — pool nhàn rỗi chưa kịp bị dọn giữa các class. Nâng lên 300
    // và giới hạn Maximum Pool Size mỗi kết nối test (xem CreateDatabaseAsync) để không chạm trần.
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine")
        .WithCommand("-c", "max_connections=300")
        .Build();
    private readonly RedisContainer _redis = new RedisBuilder("redis:7.4-alpine").Build();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        // Mỗi ApiFactory dùng connection string riêng (database riêng) nên có pool Npgsql riêng; giới hạn nhỏ
        // để tổng số kết nối vật lý cộng dồn qua nhiều test class không vượt max_connections của container.
        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString())
        {
            Database = name,
            MaxPoolSize = 10,
            MinPoolSize = 0,
        }.ConnectionString;
    }

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}

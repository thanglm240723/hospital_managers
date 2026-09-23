using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

/// Cache không TTL có race: request A đọc DB (dữ liệu cũ) → admin đổi + xoá key → A ghi lại dữ liệu cũ, tồn tại mãi.
/// Chặn bằng bộ đếm thế hệ: mỗi lần xoá key tăng {key}:gen; bên ghi chỉ ghi nếu thế hệ không đổi
/// kể từ lúc nó đọc (đọc thế hệ TRƯỚC khi truy vấn DB).
public static class GuardedCacheWrite
{
    public static readonly TimeSpan GenerationLifetime = TimeSpan.FromDays(1);

    public static Task<RedisValue> ReadGenerationAsync(IDatabase db, string key)
        => db.StringGetAsync(CacheKeys.Generation(key));

    public static Task<bool> SetIfUnchangedAsync(IDatabase db, string key, string value, RedisValue generation, TimeSpan? expiry)
    {
        var generationKey = CacheKeys.Generation(key);
        var transaction = db.CreateTransaction();
        transaction.AddCondition(generation.IsNull
            ? Condition.KeyNotExists(generationKey)
            : Condition.StringEqual(generationKey, generation));
        _ = transaction.StringSetAsync(key, value, expiry is null ? Expiration.Default : (Expiration)expiry.Value);
        return transaction.ExecuteAsync();
    }

    public static async Task InvalidateAsync(IDatabase db, IReadOnlyCollection<string> keys)
    {
        var batch = db.CreateBatch();
        var pending = new List<Task>();
        foreach (var key in keys)
        {
            var generationKey = CacheKeys.Generation(key);
            pending.Add(batch.KeyDeleteAsync(key));
            pending.Add(batch.StringIncrementAsync(generationKey));
            pending.Add(batch.KeyExpireAsync(generationKey, GenerationLifetime));
        }
        batch.Execute();
        await Task.WhenAll(pending);
    }
}

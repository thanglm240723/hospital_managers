using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

internal static class RedisFailure
{
    public static bool Is(Exception exception) => exception is RedisException or TimeoutException;
}

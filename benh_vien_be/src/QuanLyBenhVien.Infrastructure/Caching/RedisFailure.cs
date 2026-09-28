using StackExchange.Redis;

namespace QuanLyBenhVien.Infrastructure.Caching;

internal static class RedisFailure
{
    public static bool Is(Exception exception) => exception is RedisException or TimeoutException;
}

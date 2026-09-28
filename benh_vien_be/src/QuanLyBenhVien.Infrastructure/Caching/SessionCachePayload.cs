using System.Text.Json;
using System.Text.Json.Serialization;
using QuanLyBenhVien.Application.Common.Models;

namespace QuanLyBenhVien.Infrastructure.Caching;

/// JSON Gateway đọc: {"userId":"...","sv":1,"absExp":1737000000}.
internal sealed record SessionCachePayload(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("sv")] int SecurityVersion,
    [property: JsonPropertyName("absExp")] long AbsoluteExpiresAtUnix)
{
    public static string Serialize(Guid userId, int securityVersion, DateTimeOffset absoluteExpiresAtUtc)
        => JsonSerializer.Serialize(new SessionCachePayload(userId, securityVersion, absoluteExpiresAtUtc.ToUnixTimeSeconds()));

    public static string Serialize(SessionCacheEntry entry)
        => Serialize(entry.UserId, entry.SecurityVersion, entry.AbsoluteExpiresAtUtc);
}

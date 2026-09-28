using System.Text.Json;
using System.Text.Json.Serialization;
using QuanLyBenhVien.Application.Common.Models;

namespace QuanLyBenhVien.Infrastructure.Identity;

/// Giá trị perm:{userId}: {"permissions":[...],"mustChangePassword":false}
internal sealed record PermissionCachePayload(
    [property: JsonPropertyName("permissions")] string[] Permissions,
    [property: JsonPropertyName("mustChangePassword")] bool MustChangePassword)
{
    public static string Serialize(UserAccess access)
        => JsonSerializer.Serialize(new PermissionCachePayload(access.Permissions.Order(StringComparer.Ordinal).ToArray(), access.MustChangePassword));

    public static UserAccess Deserialize(string json)
    {
        var payload = JsonSerializer.Deserialize<PermissionCachePayload>(json)!;
        return new UserAccess(payload.Permissions.ToHashSet(StringComparer.Ordinal), payload.MustChangePassword);
    }
}

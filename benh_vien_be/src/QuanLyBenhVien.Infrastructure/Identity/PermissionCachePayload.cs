using System.Text.Json;
using System.Text.Json.Serialization;
using QuanLyBenhVien.Application.Common.Identity;

namespace QuanLyBenhVien.Infrastructure.Identity;

/// Giá trị perm:{userId}: {"isActive":true,"mustChangePassword":false,"permissions":[...]}
internal sealed record PermissionCachePayload(
    [property: JsonPropertyName("isActive")] bool? IsActive,
    [property: JsonPropertyName("mustChangePassword")] bool? MustChangePassword,
    [property: JsonPropertyName("permissions")] string[]? Permissions)
{
    public static string Serialize(UserAccessDto access)
        => JsonSerializer.Serialize(new PermissionCachePayload(
            access.IsActive, access.MustChangePassword, access.Permissions.Order(StringComparer.Ordinal).ToArray()));

    /// Trả null (coi như cache miss) nếu JSON hỏng hoặc thiếu trường bắt buộc, không bao giờ suy ra quyền mặc định.
    public static UserAccessDto? TryDeserialize(string json)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<PermissionCachePayload>(json);
            if (payload is not { IsActive: { } isActive, MustChangePassword: { } mustChange, Permissions: { } permissions })
                return null;
            return new UserAccessDto(isActive, mustChange, permissions.ToHashSet(StringComparer.Ordinal));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

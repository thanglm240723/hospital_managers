using Microsoft.AspNetCore.Builder;

namespace QuanLyBenhVien.Presentation.Security;

public static class PermissionPolicy
{
    public const string Prefix = "perm:";

    public static string NameFor(string permission) => Prefix + permission;
}

public static class PermissionEndpointExtensions
{
    /// Yêu cầu quyền hành động (hằng trong Domain/Identity/Permissions.cs). Chỉ gắn metadata policy `perm:<code>`;
    /// việc kiểm tra do handler ở API thực hiện.
    public static TBuilder RequirePermission<TBuilder>(this TBuilder builder, string permission) where TBuilder : IEndpointConventionBuilder
    {
        if (string.IsNullOrWhiteSpace(permission))
            throw new ArgumentException("Permission không được rỗng.", nameof(permission));
        return builder.RequireAuthorization(PermissionPolicy.NameFor(permission));
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace QuanLyBenhVien.Presentation.Auth;

public static class AuthRouteExtensions
{
    /// Route cần đăng nhập: family lấy từ claim `fid` của access token (Bearer).
    public static TBuilder RequireCsrf<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(CsrfProtectionMetadata.Bearer);
        builder.AddEndpointFilter<TBuilder, CsrfProtectionFilter>();
        return builder;
    }

    /// Route công khai dùng cookie refresh (vd. logout): family định vị qua cookie `__Host-rt`.
    /// Cookie thiếu/không nhận diện được thì không có gì để bảo vệ — filter để handler tự quyết.
    public static TBuilder RequireRefreshCookieCsrf<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(CsrfProtectionMetadata.RefreshCookie);
        builder.AddEndpointFilter<TBuilder, CsrfProtectionFilter>();
        return builder;
    }

    public static TBuilder AllowWhilePasswordChangeRequired<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(AllowWhilePasswordChangeRequiredMetadata.Instance);
}

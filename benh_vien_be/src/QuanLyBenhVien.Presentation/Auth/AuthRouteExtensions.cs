using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace QuanLyBenhVien.Presentation.Auth;

public static class AuthRouteExtensions
{
    public static TBuilder RequireCsrf<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(CsrfProtectionMetadata.Instance);
        builder.AddEndpointFilter<TBuilder, CsrfProtectionFilter>();
        return builder;
    }

    public static TBuilder AllowWhilePasswordChangeRequired<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
        => builder.WithMetadata(AllowWhilePasswordChangeRequiredMetadata.Instance);
}

namespace CleanArchCqrs.Gateway.Auth;

/// Cùng Issuer/Audience/SigningKey với API (HS256 dùng chung khoá — Spec D4).
public sealed class GatewayJwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
}

namespace CleanArchCqrs.Gateway.Auth;

public sealed class IdentityServiceOptions
{
    /// Địa chỉ nội bộ của API, ví dụ http://localhost:5289/
    public string InternalBaseUrl { get; set; } = "";
    /// Trùng Auth:InternalApiKey của API.
    public string InternalApiKey { get; set; } = "";
}

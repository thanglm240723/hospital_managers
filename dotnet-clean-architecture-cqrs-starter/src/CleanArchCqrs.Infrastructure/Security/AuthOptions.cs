namespace CleanArchCqrs.Infrastructure.Security;

public sealed class AuthOptions
{
    /// ≥ 32 ký tự, lấy từ secret store.
    public string CsrfKey { get; set; } = "";
    /// Origin được phép gọi refresh/logout và các endpoint đổi phiên (so khớp chính xác, không phân biệt hoa thường).
    public string[] AllowedOrigins { get; set; } = [];
    /// Khoá chung Gateway ↔ API cho /internal/*.
    public string InternalApiKey { get; set; } = "";
}

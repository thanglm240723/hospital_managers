namespace QuanLyBenhVien.Presentation.Http;

/// Khoá chung Gateway ↔ API cho các route `/internal/*`. Bind từ `Auth:InternalApiKey` (cùng section với
/// `QuanLyBenhVien.Infrastructure.Security.AuthOptions`).
public sealed class InternalApiOptions
{
    public string Key { get; set; } = "";
}

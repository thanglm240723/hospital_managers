namespace QuanLyBenhVien.Infrastructure.Persistence.Seed;

public sealed class SeedOptions
{
    public string? AdminEmail { get; set; }
    /// Mật khẩu tạm — Admin bị bắt đổi ở lần đăng nhập đầu. Dev: user-secrets; prod: biến môi trường.
    public string? AdminPassword { get; set; }
    public string AdminFullName { get; set; } = "Quản trị hệ thống";
}

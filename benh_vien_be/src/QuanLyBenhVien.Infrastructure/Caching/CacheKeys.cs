using System.Security.Cryptography;
using System.Text;

namespace QuanLyBenhVien.Infrastructure.Caching;

/// Định dạng key là hợp đồng với Gateway (session:*) — đổi ở đây phải đổi cả Gateway.
public static class CacheKeys
{
    public static string Session(Guid sessionFamilyId) => $"session:{sessionFamilyId}";

    public static string Permissions(Guid userId) => $"perm:{userId}";

    /// Hash email để key không chứa dữ liệu cá nhân.
    public static string LoginFailures(string normalizedEmail)
        => $"rl:email:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))}";

    public static string Generation(string key) => $"{key}:gen";
}

namespace QuanLyBenhVien.Infrastructure.Caching;

/// Định dạng key là hợp đồng với Gateway (session:*) — đổi ở đây phải đổi cả Gateway.
public static class CacheKeys
{
    public static string Session(Guid sessionFamilyId) => $"session:{sessionFamilyId}";

    public static string Permissions(Guid userId) => $"perm:{userId}";

    public static string Generation(string key) => $"{key}:gen";
}

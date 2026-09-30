using QuanLyBenhVien.Application.Common.Caching;

namespace QuanLyBenhVien.Infrastructure.Caching;

/// Định dạng key là hợp đồng với Gateway (session:*) — nguồn duy nhất ở `CacheKeyFormat` (Application).
public static class CacheKeys
{
    public static string Session(Guid sessionFamilyId) => CacheKeyFormat.Session(sessionFamilyId);

    public static string Permissions(Guid userId) => CacheKeyFormat.Permissions(userId);

    public static string Generation(string key) => $"{key}:gen";
}

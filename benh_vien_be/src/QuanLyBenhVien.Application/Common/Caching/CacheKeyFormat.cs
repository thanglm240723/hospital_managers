namespace QuanLyBenhVien.Application.Common.Caching;

/// Định dạng key là hợp đồng với Gateway (session:*) — đổi ở đây phải đổi cả Gateway.
public static class CacheKeyFormat
{
    public static string Session(Guid sessionFamilyId) => $"session:{sessionFamilyId}";

    public static string Permissions(Guid userId) => $"perm:{userId}";
}

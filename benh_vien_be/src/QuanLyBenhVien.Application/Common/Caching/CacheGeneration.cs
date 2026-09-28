namespace QuanLyBenhVien.Application.Common.Caching;

/// Giá trị mờ đại diện cho thế hệ hiện tại của key cache trong Redis — Application không biết đó là gì.
public sealed record CacheGeneration(string? Value)
{
    public static readonly CacheGeneration None = new((string?)null);
}

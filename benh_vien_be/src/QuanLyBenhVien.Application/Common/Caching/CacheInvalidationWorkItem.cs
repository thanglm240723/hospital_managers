namespace QuanLyBenhVien.Application.Common.Caching;

public sealed record CacheInvalidationWorkItem(Guid Id, string Key, int Attempts);

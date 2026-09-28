namespace QuanLyBenhVien.Application.Common.Caching;

public sealed record SessionCacheEntry(
    Guid SessionFamilyId,
    Guid UserId,
    int SecurityVersion,
    DateTimeOffset AbsoluteExpiresAtUtc);

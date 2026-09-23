namespace CleanArchCqrs.Application.Common.Models;

public sealed record SessionCacheEntry(Guid SessionFamilyId, Guid UserId, int SecurityVersion, DateTimeOffset AbsoluteExpiresAtUtc);

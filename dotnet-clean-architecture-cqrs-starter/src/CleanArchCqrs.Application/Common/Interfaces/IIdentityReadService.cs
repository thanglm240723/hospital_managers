using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Query projection (AsNoTracking) cho module IdentityAccess.
public interface IIdentityReadService
{
    Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionFamilyId, DateTimeOffset now,
        CancellationToken ct = default);
    Task<PagedResult<UserSummaryDto>> GetUsersAsync(int pageNumber, int pageSize, string? searchTerm, CancellationToken ct = default);
    Task<UserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct = default);
}

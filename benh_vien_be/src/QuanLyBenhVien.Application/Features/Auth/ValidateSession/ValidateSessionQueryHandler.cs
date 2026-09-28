using MediatR;
using QuanLyBenhVien.Application.Common.Caching;
using QuanLyBenhVien.Application.Common.Results;
using QuanLyBenhVien.Application.Features.Auth.Common;
using QuanLyBenhVien.Domain.Identity.Sessions;

namespace QuanLyBenhVien.Application.Features.Auth.ValidateSession;

internal sealed class ValidateSessionQueryHandler(IAuthReadService authRead, ISessionCache sessionCache, TimeProvider time)
    : IRequestHandler<ValidateSessionQuery, Result<SessionValidationDto>>
{
    public async Task<Result<SessionValidationDto>> Handle(ValidateSessionQuery request, CancellationToken cancellationToken)
    {
        // Đọc thế hệ cache TRƯỚC khi đọc DB (§4 A1, Q3): null nghĩa là Redis lỗi.
        var generation = await sessionCache.ReadGenerationAsync(request.SessionFamilyId, cancellationToken);
        var state = await authRead.GetSessionStateAsync(request.SessionFamilyId, cancellationToken);

        var now = time.GetUtcNow();
        var valid = state is not null
            && state.Status == SessionStatus.Active
            && state.AbsoluteExpiresAtUtc > now
            && state.IsActive
            && state.SecurityVersion == request.SecurityVersion;

        if (!valid)
        {
            return new SessionValidationDto(false, null, null);
        }

        if (generation is not null)
        {
            var entry = new SessionCacheEntry(request.SessionFamilyId, state!.UserId, state.SecurityVersion, state.AbsoluteExpiresAtUtc);
            await sessionCache.SetIfGenerationUnchangedAsync(entry, generation, cancellationToken);
        }

        return new SessionValidationDto(true, state!.UserId, state.AbsoluteExpiresAtUtc);
    }
}

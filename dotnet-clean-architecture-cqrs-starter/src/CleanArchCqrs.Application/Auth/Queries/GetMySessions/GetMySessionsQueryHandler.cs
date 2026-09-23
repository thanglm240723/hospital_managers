using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMySessions;

public sealed class GetMySessionsQueryHandler : IRequestHandler<GetMySessionsQuery, IReadOnlyList<SessionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IIdentityReadService _read;
    private readonly TimeProvider _time;

    public GetMySessionsQueryHandler(ICurrentUser currentUser, IIdentityReadService read, TimeProvider time)
    {
        _currentUser = currentUser;
        _read = read;
        _time = time;
    }

    public async Task<IReadOnlyList<SessionDto>> Handle(GetMySessionsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        return await _read.GetActiveSessionsAsync(userId, _currentUser.SessionFamilyId, _time.GetUtcNow(), ct);
    }
}

using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMe;

public sealed class GetMeQueryHandler : IRequestHandler<GetMeQuery, MeDto>
{
    private readonly ICurrentUser _currentUser;
    private readonly IIdentityReadService _read;

    public GetMeQueryHandler(ICurrentUser currentUser, IIdentityReadService read)
    {
        _currentUser = currentUser;
        _read = read;
    }

    public async Task<MeDto> Handle(GetMeQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        return await _read.GetMeAsync(userId, ct) ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
    }
}

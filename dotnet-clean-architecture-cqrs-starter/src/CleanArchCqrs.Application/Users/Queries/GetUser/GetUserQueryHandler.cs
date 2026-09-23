using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Users.Models;
using CleanArchCqrs.Domain.Exceptions;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUser;

public sealed class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDetailDto>
{
    private readonly IIdentityReadService _read;

    public GetUserQueryHandler(IIdentityReadService read) => _read = read;

    public async Task<UserDetailDto> Handle(GetUserQuery request, CancellationToken ct)
        => await _read.GetUserAsync(request.UserId, ct)
           ?? throw new NotFoundException($"User '{request.UserId}' was not found.");
}

using CleanArchCqrs.Application.Users.Models;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUser;

public sealed record GetUserQuery(Guid UserId) : IRequest<UserDetailDto>;

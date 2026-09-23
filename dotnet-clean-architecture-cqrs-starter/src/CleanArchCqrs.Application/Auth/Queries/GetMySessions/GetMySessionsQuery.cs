using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMySessions;

public sealed record GetMySessionsQuery : IRequest<IReadOnlyList<SessionDto>>;

using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMe;

public sealed record GetMeQuery : IRequest<MeDto>;

using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Refresh;

public sealed record RefreshCommand(string RefreshToken) : IRequest<AuthSession>;

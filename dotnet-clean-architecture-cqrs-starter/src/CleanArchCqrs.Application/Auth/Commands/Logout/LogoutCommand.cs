using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Logout;

public sealed record LogoutCommand(string? RefreshToken) : IRequest;

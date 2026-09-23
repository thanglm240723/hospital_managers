using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.RevokeSession;

public sealed record RevokeSessionCommand(Guid SessionId) : IRequest;

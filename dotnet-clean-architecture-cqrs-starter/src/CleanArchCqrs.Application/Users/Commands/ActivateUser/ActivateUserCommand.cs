using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.ActivateUser;

public sealed record ActivateUserCommand(Guid UserId) : IRequest;

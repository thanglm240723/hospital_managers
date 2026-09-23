using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.DeactivateUser;

public sealed record DeactivateUserCommand(Guid UserId) : IRequest;

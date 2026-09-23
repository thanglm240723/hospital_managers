using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.CreateUser;

public sealed record CreateUserCommand(string Email, string FullName, string TemporaryPassword, IReadOnlyList<Guid> RoleIds)
    : IRequest<Guid>;

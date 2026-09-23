using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.SetUserRoles;

public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : IRequest;

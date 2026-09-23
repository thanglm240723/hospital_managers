using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.CreateRole;

public sealed record CreateRoleCommand(string Code, string Name, IReadOnlyList<string> PermissionCodes) : IRequest<Guid>;

using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.RenameRole;

public sealed record RenameRoleCommand(Guid RoleId, string Name) : IRequest;

using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;

public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> PermissionCodes) : IRequest;

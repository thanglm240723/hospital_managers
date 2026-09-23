using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.GrantUserPermission;

public sealed record GrantUserPermissionCommand(Guid UserId, string PermissionCode, string Reason) : IRequest;

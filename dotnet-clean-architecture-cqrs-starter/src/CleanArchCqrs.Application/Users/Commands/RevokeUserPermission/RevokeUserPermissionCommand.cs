using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.RevokeUserPermission;

public sealed record RevokeUserPermissionCommand(Guid UserId, string PermissionCode, string Reason) : IRequest;

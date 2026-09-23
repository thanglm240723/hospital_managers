namespace CleanArchCqrs.API.Contracts.Roles;

public sealed record SetRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);

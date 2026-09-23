namespace CleanArchCqrs.API.Contracts.Users;

public sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);

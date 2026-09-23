namespace CleanArchCqrs.Application.Roles.Models;

public sealed record RoleDto(Guid Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions);

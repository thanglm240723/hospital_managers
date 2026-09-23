namespace CleanArchCqrs.API.Contracts.Users;

public sealed record PermissionGrantRequest(string PermissionCode, string Reason);

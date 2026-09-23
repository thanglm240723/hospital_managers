namespace CleanArchCqrs.Application.Users.Models;

public sealed record PermissionGrantDto(string Code, string Reason, DateTimeOffset GrantedAtUtc);

namespace CleanArchCqrs.Application.Common.Models;

public record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);
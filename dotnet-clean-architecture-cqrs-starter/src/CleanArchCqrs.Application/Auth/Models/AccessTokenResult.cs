namespace CleanArchCqrs.Application.Auth.Models;

public sealed record AccessTokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword);

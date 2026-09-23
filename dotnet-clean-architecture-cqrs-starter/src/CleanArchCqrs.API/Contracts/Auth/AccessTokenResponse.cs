namespace CleanArchCqrs.API.Contracts.Auth;

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword);

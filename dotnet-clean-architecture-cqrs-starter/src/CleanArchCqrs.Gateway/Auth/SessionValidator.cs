using System.Text.Json;
using StackExchange.Redis;

namespace CleanArchCqrs.Gateway.Auth;

/// Redis trước; không có key hoặc Redis lỗi thì hỏi API (DB là nguồn sự thật).
public sealed class SessionValidator : ISessionValidator
{
    public const string HttpClientName = "identity-internal";

    private readonly IConnectionMultiplexer _redis;
    private readonly IHttpClientFactory _httpClients;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionValidator> _logger;

    public SessionValidator(IConnectionMultiplexer redis, IHttpClientFactory httpClients, TimeProvider time,
        ILogger<SessionValidator> logger)
    {
        _redis = redis;
        _httpClients = httpClients;
        _time = time;
        _logger = logger;
    }

    public async Task<SessionCheck> ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default)
    {
        var cached = await TryReadCacheAsync(sessionFamilyId);
        if (cached is not null)
            return cached.SecurityVersion == securityVersion
                   && cached.AbsoluteExpiresAtUnix > _time.GetUtcNow().ToUnixTimeSeconds()
                ? SessionCheck.Valid
                : SessionCheck.Invalid;

        return await AskIdentityServiceAsync(sessionFamilyId, securityVersion, ct);
    }

    private async Task<SessionCachePayload?> TryReadCacheAsync(Guid sessionFamilyId)
    {
        try
        {
            var value = await _redis.GetDatabase().StringGetAsync($"session:{sessionFamilyId}");
            return value.HasValue ? JsonSerializer.Deserialize<SessionCachePayload>(value.ToString()) : null;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "Redis unavailable; validating session through the identity service");
            return null;
        }
    }

    private async Task<SessionCheck> AskIdentityServiceAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct)
    {
        try
        {
            var client = _httpClients.CreateClient(HttpClientName);
            using var response = await client.PostAsJsonAsync("internal/sessions/validate",
                new { familyId = sessionFamilyId, sv = securityVersion }, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Identity service returned {StatusCode} for session validation", (int)response.StatusCode);
                return SessionCheck.Unavailable;
            }

            var body = await response.Content.ReadFromJsonAsync<ValidateSessionResponse>(ct);
            return body?.Valid == true ? SessionCheck.Valid : SessionCheck.Invalid;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Identity service unreachable for session validation");
            return SessionCheck.Unavailable;
        }
    }

    private sealed record ValidateSessionResponse(bool Valid);
}

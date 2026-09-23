using System.Text.Json.Serialization;

namespace CleanArchCqrs.Gateway.Auth;

/// Bản sao hợp đồng với API: session:{familyId} = {"userId":"...","sv":1,"absExp":<unix seconds>}.
internal sealed record SessionCachePayload(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("sv")] int SecurityVersion,
    [property: JsonPropertyName("absExp")] long AbsoluteExpiresAtUnix);

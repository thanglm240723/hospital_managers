using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.IntegrationTests.Infrastructure;

namespace CleanArchCqrs.IntegrationTests.Helpers;

/// Mô phỏng trình duyệt: access token trong RAM, cookie __Host-rt/__Host-csrf quản lý tay
/// (HandleCookies=false để test đọc được giá trị cookie và thuộc tính Set-Cookie).
public sealed class AuthTestClient
{
    public const string RefreshCookie = "__Host-rt";
    public const string CsrfCookie = "__Host-csrf";

    public HttpClient Http { get; }
    public string? AccessToken { get; set; }
    public string? RefreshToken { get; set; }
    public string? CsrfToken { get; set; }

    public AuthTestClient(HttpClient http) => Http = http;

    /// Bản sao dùng chung cookie/token (mô phỏng 2 tab hoặc 2 request song song).
    public AuthTestClient CloneWith(HttpClient http)
        => new(http) { AccessToken = AccessToken, RefreshToken = RefreshToken, CsrfToken = CsrfToken };

    public Task<HttpResponseMessage> LoginAsync(string email, string password)
        => SendAsync(HttpMethod.Post, "/api/v1/auth/login", new { email, password }, csrf: false, bearer: false);

    public Task<HttpResponseMessage> RefreshAsync()
        => SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf: true, bearer: false);

    public Task<HttpResponseMessage> GetAsync(string path) => SendAsync(HttpMethod.Get, path, csrf: false);

    public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null,
        bool csrf = true, bool bearer = true, string? origin = TestConstants.Origin,
        IDictionary<string, string>? extraHeaders = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        if (origin is not null) request.Headers.Add("Origin", origin);

        var cookies = new List<string>();
        if (RefreshToken is not null) cookies.Add($"{RefreshCookie}={RefreshToken}");
        if (CsrfToken is not null) cookies.Add($"{CsrfCookie}={CsrfToken}");
        if (cookies.Count > 0) request.Headers.Add("Cookie", string.Join("; ", cookies));

        if (csrf && CsrfToken is not null) request.Headers.Add("X-CSRF-Token", CsrfToken);
        if (bearer && AccessToken is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", AccessToken);
        if (extraHeaders is not null)
            foreach (var (name, value) in extraHeaders) request.Headers.Add(name, value);

        var response = await Http.SendAsync(request);
        await CaptureAsync(response);
        return response;
    }

    private async Task CaptureAsync(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Set-Cookie", out var setCookies))
        {
            foreach (var setCookie in setCookies)
            {
                var pair = setCookie.Split(';')[0];
                var separator = pair.IndexOf('=');
                var name = pair[..separator];
                var value = pair[(separator + 1)..];
                if (name == RefreshCookie) RefreshToken = value.Length == 0 ? null : value;
                if (name == CsrfCookie) CsrfToken = value.Length == 0 ? null : value;
            }
        }

        if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json") return;
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("accessToken", out var accessToken))
            AccessToken = accessToken.GetString();
    }
}

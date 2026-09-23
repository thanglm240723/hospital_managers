# Giai đoạn 5 — Gateway: xác thực JWT + phiên, route v1, rate limit IP

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Gateway chặn mọi request không có JWT hợp lệ hoặc phiên đã chết trước khi tới BE: validate HS256, tra `session:{fid}` ở Redis (miss/lỗi ⇒ hỏi `/internal/sessions/validate`), route public cho login/refresh/logout, xoá header `X-Internal-*` của client, rate limit IP cho login/refresh, `/health`.

**Spec:** [`spec.md`](spec.md) §1.1, §1.2, §4.4 · **Ràng buộc chung:** [`plan.md`](plan.md#global-constraints) · **Cần xong:** Giai đoạn 3 (test "khoá tài khoản" cần Giai đoạn 4)

⚠ Gateway **không** tham chiếu project nào. `ProblemResponseWriter`, `RedisHealthCheck`, định dạng key/JSON `session:{fid}` là bản sao độc lập — giữ đúng hợp đồng ở Global Constraints.

---

### Task 5.1: Xác thực ở Gateway — JWT + phiên, route v1, bỏ header nội bộ giả

**Files:**
- Modify: `src/CleanArchCqrs.Gateway/CleanArchCqrs.Gateway.csproj`
- Create in `src/CleanArchCqrs.Gateway/Auth/`: `GatewayJwtOptions.cs`, `IdentityServiceOptions.cs`, `SessionCheck.cs`, `SessionCachePayload.cs`, `ISessionValidator.cs`, `SessionValidator.cs`, `GatewayAuthExtensions.cs`
- Create: `src/CleanArchCqrs.Gateway/Errors/ProblemResponseWriter.cs`
- Create: `src/CleanArchCqrs.Gateway/Middleware/StripInternalHeadersMiddleware.cs`
- Modify: `src/CleanArchCqrs.Gateway/Program.cs`, `appsettings.json`, `DependencyInjection/GatewayServiceExtensions.cs` (chỉ sửa XML doc), `README.md`
- Test: Create in `tests/CleanArchCqrs.IntegrationTests/Gateway/`: `RecordingHandler.cs`, `TestForwarderHttpClientFactory.cs`, `FixedRemoteIpStartupFilter.cs`, `GatewayFactory.cs`, `GatewayAuthenticationTests.cs`

**Interfaces:**
- Consumes (hợp đồng HTTP/Redis với API): `POST /internal/sessions/validate` (3.8), giá trị `session:{fid}` (3.2), claim `sub/fid/sv` (2.3).
- Produces:
  - `enum SessionCheck { Valid, Invalid, Unavailable }`; `ISessionValidator.ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default) : Task<SessionCheck>`.
  - `IServiceCollection.AddGatewayAuthentication(IConfiguration)` — JwtBearer HS256 + `OnTokenValidated` gọi `ISessionValidator`; `Invalid` ⇒ 401 `unauthenticated`; `Unavailable` ⇒ 503 `dependency_unavailable`; `FallbackPolicy` = đã xác thực.
  - `SessionValidator.HttpClientName = "identity-internal"` (timeout 2s, header `X-Internal-Key`).
  - YARP: `auth-login-route`, `auth-refresh-route`, `auth-logout-route` (POST, `AuthorizationPolicy: "anonymous"`); `auth-route`, `users-route`, `roles-route`, `permissions-route` (mặc định: phải xác thực). Route `/api/auth/**` cũ bị thay.
  - Test infra: `GatewayFactory(ApiFactory api, string redisConnectionString, IDictionary<string,string?>? overrides = null, bool identityServiceDown = false)` với `ConcurrentQueue<RecordedRequest> BackendRequests`, `IPAddress ClientIp`, `HttpClient CreateHttpsClient()`; `record RecordedRequest(string Method, string Path, IReadOnlyList<string> HeaderNames)`.

- [ ] **Step 1: Test infra (Gateway chạy trong bộ nhớ, backend là API test server thật)**

`Gateway/RecordingHandler.cs`:

```csharp
using System.Collections.Concurrent;

namespace CleanArchCqrs.IntegrationTests.Gateway;

public sealed record RecordedRequest(string Method, string Path, IReadOnlyList<string> HeaderNames);

/// Ghi lại mọi request Gateway gửi xuống API — để chứng minh request bị chặn KHÔNG tới backend.
public sealed class RecordingHandler : DelegatingHandler
{
    private readonly ConcurrentQueue<RecordedRequest> _log;

    public RecordingHandler(HttpMessageHandler inner, ConcurrentQueue<RecordedRequest> log) : base(inner) => _log = log;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        _log.Enqueue(new RecordedRequest(request.Method.Method, request.RequestUri!.AbsolutePath,
            request.Headers.Select(h => h.Key).ToList()));
        return base.SendAsync(request, ct);
    }
}
```

`Gateway/TestForwarderHttpClientFactory.cs`:

```csharp
using Yarp.ReverseProxy.Forwarder;

namespace CleanArchCqrs.IntegrationTests.Gateway;

/// YARP gửi request vào TestServer của API thay vì mạng thật.
public sealed class TestForwarderHttpClientFactory : IForwarderHttpClientFactory
{
    private readonly Func<HttpMessageHandler> _createHandler;

    public TestForwarderHttpClientFactory(Func<HttpMessageHandler> createHandler) => _createHandler = createHandler;

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(_createHandler(), disposeHandler: true);
}
```

`Gateway/FixedRemoteIpStartupFilter.cs`:

```csharp
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace CleanArchCqrs.IntegrationTests.Gateway;

/// TestServer không có IP client. Mỗi factory một IP riêng để bộ đếm rate limit IP (Redis dùng chung) không lẫn nhau.
public sealed class FixedRemoteIpStartupFilter : IStartupFilter
{
    private readonly IPAddress _ip;

    public FixedRemoteIpStartupFilter(IPAddress ip) => _ip = ip;

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, nextMiddleware) =>
        {
            context.Connection.RemoteIpAddress = _ip;
            await nextMiddleware();
        });
        next(app);
    };
}
```

`Gateway/GatewayFactory.cs`:

```csharp
using System.Collections.Concurrent;
using System.Net;
using CleanArchCqrs.Gateway.Auth;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Yarp.ReverseProxy.Forwarder;

namespace CleanArchCqrs.IntegrationTests.Gateway;

public sealed class GatewayFactory : WebApplicationFactory<CleanArchCqrs.Gateway.Program>
{
    private readonly ApiFactory _api;
    private readonly bool _identityServiceDown;
    private readonly Dictionary<string, string?> _settings;

    public ConcurrentQueue<RecordedRequest> BackendRequests { get; } = new();
    public IPAddress ClientIp { get; } = new([10, (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(256), (byte)Random.Shared.Next(1, 255)]);

    public GatewayFactory(ApiFactory api, string redisConnectionString, IDictionary<string, string?>? overrides = null,
        bool identityServiceDown = false)
    {
        _api = api;
        _identityServiceDown = identityServiceDown;
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Jwt:Issuer"] = TestConstants.JwtIssuer,
            ["Jwt:Audience"] = TestConstants.JwtAudience,
            ["Jwt:SigningKey"] = TestConstants.JwtSigningKey,
            ["Identity:InternalBaseUrl"] = "http://api/",
            ["Identity:InternalApiKey"] = TestConstants.InternalApiKey,
            ["ReverseProxy:Clusters:identity-cluster:Destinations:primary:Address"] = "http://api/",
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                _settings[key] = value;
    }

    public HttpClient CreateHttpsClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
        AllowAutoRedirect = false,
    });

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);

        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<IForwarderHttpClientFactory>(
                new TestForwarderHttpClientFactory(() => new RecordingHandler(_api.Server.CreateHandler(), BackendRequests)));
            services.AddHttpClient(SessionValidator.HttpClientName).ConfigurePrimaryHttpMessageHandler(() =>
                _identityServiceDown
                    ? new UnreachableHandler()
                    : new RecordingHandler(_api.Server.CreateHandler(), BackendRequests));
            services.AddSingleton<Microsoft.AspNetCore.Hosting.IStartupFilter>(new FixedRemoteIpStartupFilter(ClientIp));
        });
    }

    private sealed class UnreachableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => throw new HttpRequestException("Identity service unreachable (test)");
    }
}
```

- [ ] **Step 2: Viết test (đỏ)**

`Gateway/GatewayAuthenticationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Gateway;

[Collection(IntegrationCollection.Name)]
public class GatewayAuthenticationTests : IAsyncLifetime
{
    private const string Me = "/api/v1/auth/me";
    private readonly ContainersFixture _containers;
    private ApiFactory _api = default!;
    private GatewayFactory _gateway = default!;

    public GatewayAuthenticationTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _api = await ApiFactory.CreateAsync(_containers);
        _gateway = new GatewayFactory(_api, _containers.RedisConnectionString);
    }

    public async Task DisposeAsync()
    {
        await _gateway.DisposeAsync();
        await _api.DisposeAsync();
    }

    private IDatabase Redis => _api.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private int BackendCalls(string path) => _gateway.BackendRequests.Count(r => r.Path == path);

    private async Task<(string Email, AuthTestClient Client)> LoggedInViaGatewayAsync(GatewayFactory? gateway = null)
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_api, email);
        var client = new AuthTestClient((gateway ?? _gateway).CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (email, client);
    }

    private static Guid FamilyOf(string accessToken)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(accessToken).GetClaim("fid").Value);

    private static string SignedToken(string signingKey, DateTimeOffset issuedAt, TimeSpan lifetime)
        => new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = TestConstants.JwtIssuer,
            Audience = TestConstants.JwtAudience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = (issuedAt + lifetime).UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                ["sub"] = Guid.NewGuid().ToString(),
                ["fid"] = Guid.NewGuid().ToString(),
                ["sv"] = 1
            }
        });

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task LoginIsPublic_ProtectedRouteWorksWithToken()
    {
        var (_, client) = await LoggedInViaGatewayAsync();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task NoToken_Is401AtGateway_BackendNeverCalled()
    {
        var response = await new AuthTestClient(_gateway.CreateHttpsClient()).GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(response));
        Assert.Equal(0, BackendCalls(Me));
    }

    [Fact]
    public async Task ForgedSignature_Is401AtGateway()
    {
        var client = new AuthTestClient(_gateway.CreateHttpsClient())
        {
            AccessToken = SignedToken("attacker-key-0123456789-0123456789-0123456789", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15))
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
        Assert.Equal(0, BackendCalls(Me));
    }

    [Fact]
    public async Task ExpiredToken_Is401AtGateway()
    {
        var client = new AuthTestClient(_gateway.CreateHttpsClient())
        {
            AccessToken = SignedToken(TestConstants.JwtSigningKey, DateTimeOffset.UtcNow.AddMinutes(-20), TimeSpan.FromMinutes(15))
        };

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task AfterLogout_OldAccessTokenIsRejectedImmediately()
    {
        var (_, client) = await LoggedInViaGatewayAsync();
        var oldAccessToken = client.AccessToken;
        (await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false)).EnsureSuccessStatusCode();

        client.AccessToken = oldAccessToken;
        var viaGateway = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.Unauthorized, viaGateway.StatusCode);
        // API một mình chỉ kiểm chữ ký — chính Gateway là lớp thu hồi phiên ngay lập tức.
        var direct = new AuthTestClient(_api.CreateHttpsClient()) { AccessToken = oldAccessToken };
        Assert.Equal(HttpStatusCode.OK, (await direct.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task RedisMiss_GatewayAsksIdentityServiceAndRecaches()
    {
        var (_, client) = await LoggedInViaGatewayAsync();
        var key = CacheKeys.Session(FamilyOf(client.AccessToken!));
        await Redis.KeyDeleteAsync(key);

        var response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(BackendCalls("/internal/sessions/validate") >= 1);
        Assert.True(await Redis.KeyExistsAsync(key));
    }

    [Fact]
    public async Task AfterPasswordChange_TokenWithOldSecurityVersionIsRejected()
    {
        var (_, client) = await LoggedInViaGatewayAsync();
        var oldAccessToken = client.AccessToken;

        (await client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = TestData.DefaultPassword, newPassword = "Brand-New-Pass-99" })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(Me)).StatusCode);   // token mới
        client.AccessToken = oldAccessToken;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task DeactivatedAccount_OldTokenIsRejectedImmediately()
    {
        var (email, client) = await LoggedInViaGatewayAsync();
        var admin = await _api.LoginAsAdminAsync();
        var userId = await TestData.QueryAsync(_api, db => db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync());

        (await admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/deactivate")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Me)).StatusCode);
    }

    [Fact]
    public async Task ClientSuppliedInternalHeaders_AreStripped()
    {
        var (_, client) = await LoggedInViaGatewayAsync();

        await client.SendAsync(HttpMethod.Get, Me, csrf: false,
            extraHeaders: new Dictionary<string, string> { ["X-Internal-Key"] = "forged", ["X-Internal-User"] = "admin" });

        var forwarded = _gateway.BackendRequests.Last(r => r.Path == Me);
        Assert.DoesNotContain(forwarded.HeaderNames, h => h.StartsWith("X-Internal-", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task RedisMissAndIdentityServiceDown_Is503()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString, identityServiceDown: true);
        var (_, client) = await LoggedInViaGatewayAsync(gateway);
        await Redis.KeyDeleteAsync(CacheKeys.Session(FamilyOf(client.AccessToken!)));

        var response = await client.GetAsync(Me);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("dependency_unavailable", await CodeAsync(response));
    }
}
```

- [ ] **Step 3: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter GatewayAuthenticationTests`
Expected: FAIL biên dịch (`CleanArchCqrs.Gateway.Auth.SessionValidator` chưa có).

- [ ] **Step 4: Package + options + validator**

```bash
dotnet add src/CleanArchCqrs.Gateway package Microsoft.AspNetCore.Authentication.JwtBearer --version 10.0.12
dotnet add src/CleanArchCqrs.Gateway package StackExchange.Redis
```

`Auth/GatewayJwtOptions.cs`:

```csharp
namespace CleanArchCqrs.Gateway.Auth;

/// Cùng Issuer/Audience/SigningKey với API (HS256 dùng chung khoá — Spec D4).
public sealed class GatewayJwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
}
```

`Auth/IdentityServiceOptions.cs`:

```csharp
namespace CleanArchCqrs.Gateway.Auth;

public sealed class IdentityServiceOptions
{
    /// Địa chỉ nội bộ của API, ví dụ http://localhost:5289/
    public string InternalBaseUrl { get; set; } = "";
    /// Trùng Auth:InternalApiKey của API.
    public string InternalApiKey { get; set; } = "";
}
```

`Auth/SessionCheck.cs`:

```csharp
namespace CleanArchCqrs.Gateway.Auth;

public enum SessionCheck
{
    Valid,
    Invalid,
    Unavailable
}
```

`Auth/SessionCachePayload.cs`:

```csharp
using System.Text.Json.Serialization;

namespace CleanArchCqrs.Gateway.Auth;

/// Bản sao hợp đồng với API: session:{familyId} = {"userId":"...","sv":1,"absExp":<unix seconds>}.
internal sealed record SessionCachePayload(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("sv")] int SecurityVersion,
    [property: JsonPropertyName("absExp")] long AbsoluteExpiresAtUnix);
```

`Auth/ISessionValidator.cs`:

```csharp
namespace CleanArchCqrs.Gateway.Auth;

public interface ISessionValidator
{
    Task<SessionCheck> ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default);
}
```

`Auth/SessionValidator.cs`:

```csharp
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
```

`Errors/ProblemResponseWriter.cs`:

```csharp
using System.Text.Json;

namespace CleanArchCqrs.Gateway.Errors;

/// Bản sao tối giản của writer bên API — cùng hình dạng RFC 9457 { type, title, status, code, traceId }.
public static class ProblemResponseWriter
{
    public static Task WriteAsync(HttpContext context, int status, string code, string title)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].ToString();
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
            ["traceId"] = string.IsNullOrEmpty(correlationId) ? context.TraceIdentifier : correlationId,
        }));
    }
}
```

`Middleware/StripInternalHeadersMiddleware.cs`:

```csharp
namespace CleanArchCqrs.Gateway.Middleware;

/// Client không bao giờ được tự gửi header nội bộ (X-Internal-Key, X-Internal-User…) xuống backend.
public sealed class StripInternalHeadersMiddleware
{
    private const string Prefix = "X-Internal-";
    private readonly RequestDelegate _next;

    public StripInternalHeadersMiddleware(RequestDelegate next) => _next = next;

    public Task InvokeAsync(HttpContext context)
    {
        foreach (var name in context.Request.Headers.Keys
                     .Where(k => k.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)).ToList())
            context.Request.Headers.Remove(name);
        return _next(context);
    }
}
```

`Auth/GatewayAuthExtensions.cs`:

```csharp
using System.Text;
using CleanArchCqrs.Gateway.Errors;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace CleanArchCqrs.Gateway.Auth;

public static class GatewayAuthExtensions
{
    private const string DependencyUnavailableItem = "gateway.auth.dependency-unavailable";

    public static IServiceCollection AddGatewayAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<GatewayJwtOptions>(configuration.GetSection("Jwt"));
        services.Configure<IdentityServiceOptions>(configuration.GetSection("Identity"));
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "localhost:6379");
            options.AbortOnConnectFail = false;
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 1000;
            options.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(options);
        });
        services.AddHttpClient(SessionValidator.HttpClientName, (sp, client) =>
        {
            var identity = sp.GetRequiredService<IOptions<IdentityServiceOptions>>().Value;
            client.BaseAddress = new Uri(identity.InternalBaseUrl);
            client.Timeout = TimeSpan.FromSeconds(2);
            client.DefaultRequestHeaders.Add("X-Internal-Key", identity.InternalApiKey);
        });
        services.AddSingleton<ISessionValidator, SessionValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<GatewayJwtOptions>>((options, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = "sub",
                };
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = ValidateSessionAsync,
                    OnChallenge = WriteChallengeAsync,
                };
            });

        // Mọi route mặc định phải xác thực; route public khai báo AuthorizationPolicy = "anonymous" trong YARP.
        services.AddAuthorization(options =>
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        return services;
    }

    private static async Task ValidateSessionAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirst("fid")?.Value, out var familyId)
            || !int.TryParse(principal.FindFirst("sv")?.Value, out var securityVersion))
        {
            context.Fail("Token is missing session claims.");
            return;
        }

        var result = await context.HttpContext.RequestServices.GetRequiredService<ISessionValidator>()
            .ValidateAsync(familyId, securityVersion, context.HttpContext.RequestAborted);
        if (result == SessionCheck.Valid) return;

        if (result == SessionCheck.Unavailable)
            context.HttpContext.Items[DependencyUnavailableItem] = true;
        context.Fail("Session is no longer valid.");
    }

    private static async Task WriteChallengeAsync(JwtBearerChallengeContext context)
    {
        context.HandleResponse();
        if (context.HttpContext.Items.ContainsKey(DependencyUnavailableItem))
            await ProblemResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status503ServiceUnavailable,
                "dependency_unavailable", "Dịch vụ xác thực tạm thời không khả dụng.");
        else
            await ProblemResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                "unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.");
    }
}
```

- [ ] **Step 5: Route + Program**

`appsettings.json` — trong `ReverseProxy.Routes`, **xoá** `auth-route` cũ (`/api/auth/{**catch-all}`) và thêm:

```json
      "auth-login-route": {
        "ClusterId": "identity-cluster",
        "Order": 0,
        "AuthorizationPolicy": "anonymous",
        "Match": { "Path": "/api/v1/auth/login", "Methods": [ "POST" ] }
      },
      "auth-refresh-route": {
        "ClusterId": "identity-cluster",
        "Order": 0,
        "AuthorizationPolicy": "anonymous",
        "Match": { "Path": "/api/v1/auth/refresh", "Methods": [ "POST" ] }
      },
      "auth-logout-route": {
        "ClusterId": "identity-cluster",
        "Order": 0,
        "AuthorizationPolicy": "anonymous",
        "Match": { "Path": "/api/v1/auth/logout", "Methods": [ "POST" ] }
      },
      "auth-route": {
        "ClusterId": "identity-cluster",
        "Order": 1,
        "Match": { "Path": "/api/v1/auth/{**catch-all}" }
      },
      "users-route": {
        "ClusterId": "identity-cluster",
        "Order": 1,
        "Match": { "Path": "/api/v1/users/{**catch-all}" }
      },
      "roles-route": {
        "ClusterId": "identity-cluster",
        "Order": 1,
        "Match": { "Path": "/api/v1/roles/{**catch-all}" }
      },
      "permissions-route": {
        "ClusterId": "identity-cluster",
        "Order": 1,
        "Match": { "Path": "/api/v1/permissions/{**catch-all}" }
      },
```

Và thêm ở gốc file:

```json
  "ConnectionStrings": { "Redis": "localhost:6379" },
  "Jwt": { "Issuer": "http://localhost:5289", "Audience": "hospital-management", "SigningKey": "" },
  "Identity": { "InternalBaseUrl": "http://localhost:5289/", "InternalApiKey": "" },
```

(`Jwt:Issuer`/`Audience` phải trùng `src/CleanArchCqrs.API/appsettings.json`.) Secret dev — **cùng giá trị** đã đặt cho API ở Task 3.1:

```bash
dotnet user-secrets init --project src/CleanArchCqrs.Gateway
dotnet user-secrets set "Jwt:SigningKey" "<giống Jwt:SigningKey của API>" --project src/CleanArchCqrs.Gateway
dotnet user-secrets set "Identity:InternalApiKey" "<giống Auth:InternalApiKey của API>" --project src/CleanArchCqrs.Gateway
```

`Program.cs` (toàn bộ):

```csharp
using CleanArchCqrs.Gateway.Auth;
using CleanArchCqrs.Gateway.DependencyInjection;
using CleanArchCqrs.Gateway.Middleware;
using Serilog;

namespace CleanArchCqrs.Gateway;

/// <summary>
/// API Gateway — điểm vào công khai duy nhất. Xác thực JWT + phiên (Redis → API nội bộ) rồi mới định tuyến.
/// Phân quyền và nghiệp vụ nằm ở backend.
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services));

        builder.Services.AddGatewayReverseProxy(builder.Configuration);
        builder.Services.AddGatewayAuthentication(builder.Configuration);

        var app = builder.Build();

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<StripInternalHeadersMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapReverseProxy();

        app.Run();
    }
}
```

`DependencyInjection/GatewayServiceExtensions.cs` — sửa XML doc của `AddGatewayReverseProxy` cho đúng hiện trạng:

```csharp
    /// <summary>
    /// Registers YARP with the routes and clusters declared in the "ReverseProxy" configuration section.
    /// The Authorization header is forwarded unchanged; the API re-validates the JWT signature as defence in depth.
    /// </summary>
```

`README.md` của Gateway — thay đoạn "Tầng này chỉ định tuyến… backend tự phát hành và tự validate token" bằng:

```markdown
Tầng này **xác thực** rồi mới định tuyến: validate JWT HS256 (cùng khoá với API) và kiểm tra phiên
`session:{fid}` trong Redis — không có key hoặc Redis lỗi thì hỏi `POST /internal/sessions/validate` của API.
Route public (không cần token): `POST /api/v1/auth/login|refresh|logout`. Mọi route khác phải có token hợp lệ.
**Phân quyền** vẫn do API quyết định. Header `X-Internal-*` do client gửi bị xoá trước khi forward.
Chạy API bằng profile `http` (Gateway gọi `http://localhost:5289`).
```

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter GatewayAuthenticationTests`
Expected: PASS 10/10.

- [ ] **Step 7: Commit**

```bash
git add -A src/CleanArchCqrs.Gateway tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(gateway): authenticate JWT and live session at the edge, v1 identity routes, strip internal headers"
```

---

### Task 5.2: Rate limit theo IP, forwarded headers, `/health`

**Files:**
- Create: `src/CleanArchCqrs.Gateway/Middleware/IpRateLimitMiddleware.cs`
- Create: `src/CleanArchCqrs.Gateway/HealthChecks/RedisHealthCheck.cs`
- Modify: `src/CleanArchCqrs.Gateway/Program.cs`, `appsettings.json`
- Test: `tests/CleanArchCqrs.IntegrationTests/Gateway/GatewayRateLimitAndHealthTests.cs`

**Interfaces:**
- Consumes: `IConnectionMultiplexer` (5.1), `ProblemResponseWriter` (5.1).
- Produces: `IpRateLimitMiddleware` — `POST /api/v1/auth/login` 10/phút/IP, `POST /api/v1/auth/refresh` 30/phút/IP, fixed window Redis `rl:ip:{login|refresh}:{ip}`; vượt ⇒ 429 `rate_limited` + `Retry-After`; Redis lỗi ⇒ cho qua. `GET /health` (ẩn danh; Redis lỗi ⇒ `Degraded`). Chỉ tin `X-Forwarded-For` từ `ForwardedHeaders:KnownProxies`.

- [ ] **Step 1: Viết test (đỏ)**

`Gateway/GatewayRateLimitAndHealthTests.cs`:

```csharp
using System.Net;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Gateway;

[Collection(IntegrationCollection.Name)]
public class GatewayRateLimitAndHealthTests : IAsyncLifetime
{
    private const string Login = "/api/v1/auth/login";
    private readonly ContainersFixture _containers;
    private ApiFactory _api = default!;

    public GatewayRateLimitAndHealthTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _api = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _api.DisposeAsync();

    [Fact]
    public async Task EleventhLoginFromSameIpWithinAMinute_Is429AtGateway()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString);
        var client = new AuthTestClient(gateway.CreateHttpsClient());

        for (var i = 0; i < 10; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(TestData.NewEmail(), "Wrong-Password-1")).StatusCode);
        var eleventh = await client.LoginAsync(TestData.NewEmail(), "Wrong-Password-1");

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
        Assert.True(int.Parse(eleventh.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Equal(10, gateway.BackendRequests.Count(r => r.Path == Login));
    }

    [Fact]
    public async Task RedisDown_RateLimitIsSkippedAndHealthIsDegraded()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var client = new AuthTestClient(gateway.CreateHttpsClient());

        for (var i = 0; i < 11; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.LoginAsync(TestData.NewEmail(), "Wrong-Password-1")).StatusCode);

        var health = await gateway.CreateHttpsClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Degraded", await health.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_IsAnonymousAndHealthy()
    {
        await using var gateway = new GatewayFactory(_api, _containers.RedisConnectionString);

        var health = await gateway.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Equal("Healthy", await health.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter GatewayRateLimitAndHealthTests`
Expected: FAIL — không có 429; `/health` bị 401 (fallback policy).

- [ ] **Step 3: Code**

`Middleware/IpRateLimitMiddleware.cs`:

```csharp
using CleanArchCqrs.Gateway.Errors;
using StackExchange.Redis;

namespace CleanArchCqrs.Gateway.Middleware;

/// Chặn dò mật khẩu/spam refresh theo IP trước khi request tới backend. Bộ đếm ở Redis nên dùng được nhiều instance.
public sealed class IpRateLimitMiddleware
{
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    private static readonly (string Path, string Name, int Limit)[] Rules =
    [
        ("/api/v1/auth/login", "login", 10),
        ("/api/v1/auth/refresh", "refresh", 30),
    ];

    private readonly RequestDelegate _next;
    private readonly ILogger<IpRateLimitMiddleware> _logger;

    public IpRateLimitMiddleware(RequestDelegate next, ILogger<IpRateLimitMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, IConnectionMultiplexer redis)
    {
        var rule = HttpMethods.IsPost(context.Request.Method)
            ? Rules.FirstOrDefault(r => context.Request.Path.Equals(r.Path, StringComparison.OrdinalIgnoreCase))
            : default;
        if (rule.Path is null)
        {
            await _next(context);
            return;
        }

        var key = $"rl:ip:{rule.Name}:{context.Connection.RemoteIpAddress?.ToString() ?? "unknown"}";
        try
        {
            var db = redis.GetDatabase();
            var count = await db.StringIncrementAsync(key);
            await db.KeyExpireAsync(key, Window, ExpireWhen.HasNoExpiry);
            if (count > rule.Limit)
            {
                var retryAfter = await db.KeyTimeToLiveAsync(key) ?? Window;
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString();
                await ProblemResponseWriter.WriteAsync(context, StatusCodes.Status429TooManyRequests, "rate_limited",
                    "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau.");
                return;
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            _logger.LogWarning(ex, "IP rate limiter unavailable; allowing request");
        }

        await _next(context);
    }
}
```

`HealthChecks/RedisHealthCheck.cs`:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CleanArchCqrs.Gateway.HealthChecks;

/// Bản sao của health check bên API (Gateway không tham chiếu Infrastructure).
public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _redis;

    public RedisHealthCheck(IConnectionMultiplexer redis) => _redis = redis;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().PingAsync();
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return new HealthCheckResult(context.Registration.FailureStatus, "Redis unreachable", ex);
        }
    }
}
```

`Program.cs` — thêm `using System.Net;`, `using CleanArchCqrs.Gateway.HealthChecks;`, `using Microsoft.AspNetCore.HttpOverrides;`, `using Microsoft.Extensions.Diagnostics.HealthChecks;`; sau `AddGatewayAuthentication(...)` thêm:

```csharp
        builder.Services.AddHealthChecks()
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded);

        // Chỉ tin X-Forwarded-For từ load balancer đã khai báo — không thì client tự khai IP để né rate limit.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
```

Pipeline thành:

```csharp
        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseMiddleware<StripInternalHeadersMiddleware>();
        app.UseSerilogRequestLogging();
        app.UseMiddleware<IpRateLimitMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapHealthChecks("/health").AllowAnonymous();
        app.MapReverseProxy();
```

`appsettings.json` thêm ở gốc:

```json
  "ForwardedHeaders": { "KnownProxies": [] },
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter "GatewayRateLimitAndHealthTests|GatewayAuthenticationTests"`
Expected: PASS 13/13.

Run: `dotnet build && dotnet test`
Expected: 0 warning; toàn bộ xanh.

- [ ] **Step 5: Kiểm tra tay end-to-end**

```bash
docker compose up -d
dotnet run --project src/CleanArchCqrs.API --launch-profile http        # terminal 1
dotnet run --project src/CleanArchCqrs.Gateway --launch-profile http    # terminal 2
curl -i -X POST http://localhost:5100/api/v1/auth/login -H "Content-Type: application/json" \
  -d '{"email":"admin@hospital.local","password":"<Seed:AdminPassword>"}'
curl -i http://localhost:5100/api/v1/auth/me
```

Expected: login 200 + 2 `Set-Cookie`; gọi `me` không token → 401 `application/problem+json` từ Gateway; Seq có log `Service = gateway` và `Service = api` chung `CorrelationId`.

- [ ] **Step 6: Commit**

```bash
git add -A src/CleanArchCqrs.Gateway tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(gateway): per-IP rate limiting for login/refresh, trusted forwarded headers, health endpoint"
```

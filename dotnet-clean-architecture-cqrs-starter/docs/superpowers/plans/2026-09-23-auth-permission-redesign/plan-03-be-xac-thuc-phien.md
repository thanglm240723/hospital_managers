# Giai đoạn 3 — BE: xác thực & phiên

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** API phát và quản lý phiên thật: `login`, `refresh` (rotation + strict reuse), `logout`, `logout-all`, `me`, `sessions`, `sessions/{id}/revoke`, `change-password`, và endpoint nội bộ `/internal/sessions/validate` cho Gateway; cookie `__Host-rt`/`__Host-csrf`, CSRF, rate limit email, audit, cache phiên Redis, xoá cache tin cậy.

**Spec:** [`spec.md`](spec.md) §1.2, §3, §4.3, §6.1 · **Ràng buộc chung:** [`plan.md`](plan.md#global-constraints) · **Cần xong:** Giai đoạn 2

⚠ Ở giai đoạn này **chưa** có `[HasPermission]` và chưa chặn `MustChangePassword` (Giai đoạn 4). Endpoint cần đăng nhập dùng `[Authorize]` ở cấp controller.

---

### Task 3.1: Nguyên liệu bảo mật — refresh token, CSRF, cân bằng thời gian login

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Models/GeneratedRefreshToken.cs`
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/IRefreshTokenGenerator.cs`, `ICsrfTokenService.cs`
- Modify: `src/CleanArchCqrs.Application/Common/Interfaces/IPasswordHasher.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Security/AuthOptions.cs`, `RefreshTokenGenerator.cs`, `CsrfTokenService.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/Security/PasswordHasher.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Modify: `src/CleanArchCqrs.API/appsettings.json`
- Test: Create `tests/CleanArchCqrs.UnitTests/Infrastructure/Security/RefreshTokenGeneratorTests.cs`, `CsrfTokenServiceTests.cs`; Modify `PasswordHasherTests.cs`

**Interfaces:**
- Consumes: —
- Produces:
  - `record GeneratedRefreshToken(string Token, string Hash)`.
  - `IRefreshTokenGenerator { GeneratedRefreshToken Generate(); string Hash(string token); }` — token 32 byte base64url (43 ký tự), hash SHA-256 hex lowercase.
  - `ICsrfTokenService { string Create(Guid sessionFamilyId); bool IsValid(Guid sessionFamilyId, string? token); }` — HMAC-SHA256(familyId) base64url, so sánh hằng thời gian.
  - `IPasswordHasher.SimulateVerify(string password)` — tốn thời gian như `Verify`, dùng khi email không tồn tại.
  - `AuthOptions { string CsrfKey; string[] AllowedOrigins; string InternalApiKey; }` (section `Auth`).
  - DI singleton: `IRefreshTokenGenerator`, `ICsrfTokenService`.

- [ ] **Step 1: Viết test (đỏ)**

`RefreshTokenGeneratorTests.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Security;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class RefreshTokenGeneratorTests
{
    private readonly RefreshTokenGenerator _generator = new();

    [Fact]
    public void Generate_Returns256BitBase64UrlTokenAndItsHash()
    {
        var generated = _generator.Generate();

        Assert.Matches("^[A-Za-z0-9_-]{43}$", generated.Token);
        Assert.Matches("^[0-9a-f]{64}$", generated.Hash);
        Assert.Equal(_generator.Hash(generated.Token), generated.Hash);
    }

    [Fact]
    public void Generate_ProducesDifferentTokensEachTime()
        => Assert.NotEqual(_generator.Generate().Token, _generator.Generate().Token);
}
```

`CsrfTokenServiceTests.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class CsrfTokenServiceTests
{
    private static CsrfTokenService Create(string key = "csrf-key-0123456789-0123456789-0123")
        => new(Options.Create(new AuthOptions { CsrfKey = key }));

    [Fact]
    public void TokenIsBoundToItsSessionFamily()
    {
        var service = Create();
        var familyId = Guid.NewGuid();

        var token = service.Create(familyId);

        Assert.True(service.IsValid(familyId, token));
        Assert.False(service.IsValid(Guid.NewGuid(), token));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64 !!")]
    [InlineData("AAAA")]
    public void InvalidTokens_AreRejected(string? token)
        => Assert.False(Create().IsValid(Guid.NewGuid(), token));

    [Fact]
    public void DifferentKey_ProducesDifferentToken()
    {
        var familyId = Guid.NewGuid();

        Assert.False(Create("another-key-0123456789-0123456789-01").IsValid(familyId, Create().Create(familyId)));
    }

    [Fact]
    public void ShortKey_Throws()
        => Assert.Throws<InvalidOperationException>(() => Create("short"));
}
```

Trong `PasswordHasherTests.cs` thêm:

```csharp
    [Fact]
    public void SimulateVerify_DoesNotThrowForAnyInput()
    {
        var hasher = new PasswordHasher();

        hasher.SimulateVerify("anything");
        hasher.SimulateVerify("");
    }
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "RefreshTokenGeneratorTests|CsrfTokenServiceTests|PasswordHasherTests"`
Expected: FAIL biên dịch.

- [ ] **Step 3: Hợp đồng Application**

`Common/Models/GeneratedRefreshToken.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Models;

/// Token gửi cho trình duyệt (cookie) và hash lưu DB. Không bao giờ lưu Token.
public sealed record GeneratedRefreshToken(string Token, string Hash);
```

`Common/Interfaces/IRefreshTokenGenerator.cs`:

```csharp
using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

public interface IRefreshTokenGenerator
{
    GeneratedRefreshToken Generate();
    string Hash(string token);
}
```

`Common/Interfaces/ICsrfTokenService.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Interfaces;

/// CSRF token có chữ ký gắn với phiên (SessionFamily) — Đặc tả kỹ thuật §4.1.
public interface ICsrfTokenService
{
    string Create(Guid sessionFamilyId);
    bool IsValid(Guid sessionFamilyId, string? token);
}
```

`IPasswordHasher.cs` — thêm vào interface:

```csharp
        /// Chạy một phép verify giả để nhánh "email không tồn tại" tốn thời gian như nhánh "sai mật khẩu".
        void SimulateVerify(string password);
```

- [ ] **Step 4: Bản cài Infrastructure**

`Security/AuthOptions.cs`:

```csharp
namespace CleanArchCqrs.Infrastructure.Security;

public sealed class AuthOptions
{
    /// ≥ 32 ký tự, lấy từ secret store.
    public string CsrfKey { get; set; } = "";
    /// Origin được phép gọi refresh/logout và các endpoint đổi phiên (so khớp chính xác, không phân biệt hoa thường).
    public string[] AllowedOrigins { get; set; } = [];
    /// Khoá chung Gateway ↔ API cho /internal/*.
    public string InternalApiKey { get; set; } = "";
}
```

`Security/RefreshTokenGenerator.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class RefreshTokenGenerator : IRefreshTokenGenerator
{
    private const int TokenBytes = 32;

    public GeneratedRefreshToken Generate()
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(TokenBytes));
        return new GeneratedRefreshToken(token, Hash(token));
    }

    public string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
```

`Security/CsrfTokenService.cs`:

```csharp
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class CsrfTokenService : ICsrfTokenService
{
    private const int MinKeyLength = 32;
    private readonly byte[] _key;

    public CsrfTokenService(IOptions<AuthOptions> options)
    {
        var key = options.Value.CsrfKey;
        if (string.IsNullOrEmpty(key) || key.Length < MinKeyLength)
            throw new InvalidOperationException($"Auth:CsrfKey must be at least {MinKeyLength} characters.");
        _key = Encoding.UTF8.GetBytes(key);
    }

    public string Create(Guid sessionFamilyId) => Base64Url.EncodeToString(Compute(sessionFamilyId));

    public bool IsValid(Guid sessionFamilyId, string? token)
    {
        if (string.IsNullOrEmpty(token)) return false;
        byte[] provided;
        try
        {
            provided = Base64Url.DecodeFromChars(token);
        }
        catch (FormatException)
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(provided, Compute(sessionFamilyId));
    }

    private byte[] Compute(Guid sessionFamilyId) => HMACSHA256.HashData(_key, sessionFamilyId.ToByteArray());
}
```

`Security/PasswordHasher.cs` — thêm field và method:

```csharp
    private static readonly string DummyHash =
        new Microsoft.AspNetCore.Identity.PasswordHasher<User>().HashPassword(default!, "timing-equalizer-not-a-real-password");

    public void SimulateVerify(string password) => _hasher.VerifyHashedPassword(default!, DummyHash, password);
```

DI — trong `AddInfrastructureServices` thêm:

```csharp
        services.Configure<AuthOptions>(configuration.GetSection("Auth"));
        services.AddSingleton<IRefreshTokenGenerator, RefreshTokenGenerator>();
        services.AddSingleton<ICsrfTokenService, CsrfTokenService>();
```

`API/appsettings.json` thêm ở gốc:

```json
  "Auth": {
    "AllowedOrigins": [ "http://localhost:9000" ],
    "CsrfKey": "",
    "InternalApiKey": ""
  },
```

Đặt secret dev (không commit; sinh chuỗi ngẫu nhiên riêng của bạn, ≥ 32 ký tự):

```bash
dotnet user-secrets set "Jwt:SigningKey" "<chuỗi ngẫu nhiên ≥ 32 ký tự>" --project src/CleanArchCqrs.API
dotnet user-secrets set "Auth:CsrfKey" "<chuỗi ngẫu nhiên ≥ 32 ký tự khác>" --project src/CleanArchCqrs.API
dotnet user-secrets set "Auth:InternalApiKey" "<chuỗi ngẫu nhiên ≥ 32 ký tự khác>" --project src/CleanArchCqrs.API
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "RefreshTokenGeneratorTests|CsrfTokenServiceTests|PasswordHasherTests"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.UnitTests
git commit -m "feat(security): add refresh token generator, session-bound CSRF tokens, login timing equalizer"
```

---

### Task 3.2: Dịch vụ Redis — cache phiên, rate limit email, xoá cache tin cậy

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Models/SessionCacheEntry.cs`
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/ISessionCache.cs`, `ILoginRateLimiter.cs`, `ICacheInvalidator.cs`
- Create in `src/CleanArchCqrs.Infrastructure/Caching/`: `CacheKeys.cs`, `RedisFailure.cs`, `GuardedCacheWrite.cs`, `SessionCachePayload.cs`, `SessionCache.cs`, `LoginRateLimiter.cs`, `CacheInvalidator.cs`, `CacheInvalidationProcessor.cs`, `CacheInvalidationWorker.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Test: Create `tests/CleanArchCqrs.IntegrationTests/Helpers/TestServices.cs`, `tests/CleanArchCqrs.IntegrationTests/Caching/RedisServicesTests.cs`

**Interfaces:**
- Consumes: `CacheInvalidation` (2.6), `AppDbContext`, `IConnectionMultiplexer` (1.2), `TimeProvider`.
- Produces:
  - `record SessionCacheEntry(Guid SessionFamilyId, Guid UserId, int SecurityVersion, DateTimeOffset AbsoluteExpiresAtUtc)`.
  - `ISessionCache.SetAsync(SessionCacheEntry entry, CancellationToken ct = default)` — **chỉ dùng lúc login** (family mới); Redis lỗi thì log, không ném.
  - `ILoginRateLimiter`: `Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, ct)` (null = không bị chặn), `Task RegisterFailureAsync(string normalizedEmail, ct)`, `Task ResetAsync(string normalizedEmail, ct)`; hằng `LoginRateLimiter.MaxFailures = 5`, `LoginRateLimiter.Window = 15'`. Redis lỗi ⇒ không chặn.
  - `ICacheInvalidator` (scoped): `void InvalidateSession(Guid sessionFamilyId)`, `void InvalidatePermissions(Guid userId)` — chèn dòng `CacheInvalidations` vào DbContext hiện tại (được lưu cùng `SaveChangesAsync` nghiệp vụ); `Task FlushAsync(CancellationToken ct = default)` — gọi **sau khi commit**.
  - `CacheKeys` (public static): `Session(Guid)`, `Permissions(Guid)`, `LoginFailures(string normalizedEmail)`, `Generation(string key)`.
  - `GuardedCacheWrite` (public static): `ReadGenerationAsync(IDatabase, string key)`, `SetIfUnchangedAsync(IDatabase, string key, string value, RedisValue generation, TimeSpan? expiry) : Task<bool>`, `InvalidateAsync(IDatabase, IReadOnlyCollection<string> keys)`.
  - `CacheInvalidationProcessor.ProcessPendingAsync(int batchSize, CancellationToken ct = default) : Task<int>`; `CacheInvalidationWorker : BackgroundService` (chạy ngay khi khởi động, rồi mỗi 5s, lô 100).
  - Test helper: `TestServices.RemoveCacheInvalidationWorker(IServiceCollection)`.

- [ ] **Step 1: Viết test (đỏ)**

`Helpers/TestServices.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Caching;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.IntegrationTests.Helpers;

public static class TestServices
{
    /// Tắt worker để test tự điều khiển thời điểm xử lý bảng CacheInvalidations.
    public static void RemoveCacheInvalidationWorker(IServiceCollection services)
        => services.Remove(services.Single(d => d.ImplementationType == typeof(CacheInvalidationWorker)));
}
```

`Caching/RedisServicesTests.cs`:

```csharp
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Caching;

[Collection(IntegrationCollection.Name)]
public class RedisServicesTests
{
    private static readonly Dictionary<string, string?> RedisDown = new() { ["ConnectionStrings:Redis"] = "127.0.0.1:1" };
    private readonly ContainersFixture _containers;

    public RedisServicesTests(ContainersFixture containers) => _containers = containers;

    private Task<ApiFactory> CreateAsync(Dictionary<string, string?>? overrides = null)
        => ApiFactory.CreateAsync(_containers, overrides, TestServices.RemoveCacheInvalidationWorker);

    private static IDatabase Redis(ApiFactory f) => f.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    [Fact]
    public async Task SessionCache_WritesGatewayJsonWithTtlUntilAbsoluteExpiry()
    {
        await using var factory = await CreateAsync();
        var entry = new SessionCacheEntry(Guid.NewGuid(), Guid.NewGuid(), 3, DateTimeOffset.UtcNow.AddHours(1));

        await factory.Services.GetRequiredService<ISessionCache>().SetAsync(entry);

        var key = CacheKeys.Session(entry.SessionFamilyId);
        using var json = JsonDocument.Parse((string)(await Redis(factory).StringGetAsync(key))!);
        Assert.Equal(entry.UserId.ToString(), json.RootElement.GetProperty("userId").GetString());
        Assert.Equal(3, json.RootElement.GetProperty("sv").GetInt32());
        Assert.Equal(entry.AbsoluteExpiresAtUtc.ToUnixTimeSeconds(), json.RootElement.GetProperty("absExp").GetInt64());
        Assert.InRange((await Redis(factory).KeyTimeToLiveAsync(key))!.Value, TimeSpan.FromMinutes(59), TimeSpan.FromMinutes(60));
    }

    [Fact]
    public async Task SessionCache_RedisDown_DoesNotThrow()
    {
        await using var factory = await CreateAsync(RedisDown);

        await factory.Services.GetRequiredService<ISessionCache>()
            .SetAsync(new SessionCacheEntry(Guid.NewGuid(), Guid.NewGuid(), 1, DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task LoginRateLimiter_LocksAfterFiveFailuresAndResetClears()
    {
        await using var factory = await CreateAsync();
        var limiter = factory.Services.GetRequiredService<ILoginRateLimiter>();
        var email = $"rl-{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < 4; i++) await limiter.RegisterFailureAsync(email);
        Assert.Null(await limiter.GetLockoutRemainingAsync(email));

        await limiter.RegisterFailureAsync(email);
        Assert.InRange((await limiter.GetLockoutRemainingAsync(email))!.Value, TimeSpan.FromMinutes(14), TimeSpan.FromMinutes(15));

        await limiter.ResetAsync(email);
        Assert.Null(await limiter.GetLockoutRemainingAsync(email));
    }

    [Fact]
    public async Task LoginRateLimiter_RedisDown_NeverLocks()
    {
        await using var factory = await CreateAsync(RedisDown);
        var limiter = factory.Services.GetRequiredService<ILoginRateLimiter>();

        for (var i = 0; i < 6; i++) await limiter.RegisterFailureAsync("x@test.local");

        Assert.Null(await limiter.GetLockoutRemainingAsync("x@test.local"));
    }

    [Fact]
    public async Task CacheInvalidator_FlushAfterCommit_DeletesKeyRowAndBumpsGeneration()
    {
        await using var factory = await CreateAsync();
        var userId = Guid.NewGuid();
        var key = CacheKeys.Permissions(userId);
        await Redis(factory).StringSetAsync(key, "stale");

        await using var scope = factory.Services.CreateAsyncScope();
        var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        invalidator.InvalidatePermissions(userId);
        await db.SaveChangesAsync();
        await invalidator.FlushAsync();

        Assert.False(await Redis(factory).KeyExistsAsync(key));
        Assert.Equal(1, (int)await Redis(factory).StringGetAsync(CacheKeys.Generation(key)));
        Assert.False(await db.CacheInvalidations.AnyAsync(c => c.Key == key));
    }

    [Fact]
    public async Task CacheInvalidator_RedisDown_KeepsRowAndProcessorCountsAttempt()
    {
        await using var factory = await CreateAsync(RedisDown);
        await using var scope = factory.Services.CreateAsyncScope();
        var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var familyId = Guid.NewGuid();
        var key = CacheKeys.Session(familyId);

        invalidator.InvalidateSession(familyId);
        await db.SaveChangesAsync();
        await invalidator.FlushAsync();
        await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>().ProcessPendingAsync(100);

        var row = await db.CacheInvalidations.AsNoTracking().SingleAsync(c => c.Key == key);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.LastError);
    }

    [Fact]
    public async Task Processor_DeletesPendingKeys()
    {
        await using var factory = await CreateAsync();
        var key = CacheKeys.Permissions(Guid.NewGuid());
        await Redis(factory).StringSetAsync(key, "stale");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.CacheInvalidations.Add(CacheInvalidation.Create(key, DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();

        var processed = await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>().ProcessPendingAsync(100);

        Assert.Equal(1, processed);
        Assert.False(await Redis(factory).KeyExistsAsync(key));
        Assert.False(await db.CacheInvalidations.AsNoTracking().AnyAsync(c => c.Key == key));
    }

    [Fact]
    public async Task GuardedWrite_SkipsWriteWhenKeyWasInvalidatedAfterGenerationRead()
    {
        await using var factory = await CreateAsync();
        var redis = Redis(factory);
        var key = CacheKeys.Permissions(Guid.NewGuid());

        var generation = await GuardedCacheWrite.ReadGenerationAsync(redis, key);   // trước khi đọc DB
        await GuardedCacheWrite.InvalidateAsync(redis, [key]);                        // admin đổi quyền giữa chừng
        var written = await GuardedCacheWrite.SetIfUnchangedAsync(redis, key, "stale", generation, null);

        Assert.False(written);
        Assert.False(await redis.KeyExistsAsync(key));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter RedisServicesTests`
Expected: FAIL biên dịch.

- [ ] **Step 3: Hợp đồng Application**

`Common/Models/SessionCacheEntry.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Models;

public sealed record SessionCacheEntry(Guid SessionFamilyId, Guid UserId, int SecurityVersion, DateTimeOffset AbsoluteExpiresAtUtc);
```

`Common/Interfaces/ISessionCache.cs`:

```csharp
using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Ghi session:{familyId} cho Gateway đọc. Chỉ gọi cho family vừa tạo (login).
/// Các trường hợp khác: xoá key qua ICacheInvalidator, Gateway tự nạp lại qua BE.
public interface ISessionCache
{
    Task SetAsync(SessionCacheEntry entry, CancellationToken ct = default);
}
```

`Common/Interfaces/ILoginRateLimiter.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Interfaces;

public interface ILoginRateLimiter
{
    /// null nếu email chưa bị chặn.
    Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, CancellationToken ct = default);
    Task RegisterFailureAsync(string normalizedEmail, CancellationToken ct = default);
    Task ResetAsync(string normalizedEmail, CancellationToken ct = default);
}
```

`Common/Interfaces/ICacheInvalidator.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Interfaces;

/// Invalidate*: ghi lệnh xoá vào DB cùng transaction nghiệp vụ (không bao giờ thất lạc).
/// FlushAsync: gọi SAU khi commit để xoá Redis ngay; lỗi thì worker làm lại.
public interface ICacheInvalidator
{
    void InvalidateSession(Guid sessionFamilyId);
    void InvalidatePermissions(Guid userId);
    Task FlushAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Bản cài Infrastructure**

`Caching/CacheKeys.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace CleanArchCqrs.Infrastructure.Caching;

/// Định dạng key là hợp đồng với Gateway (session:*) — đổi ở đây phải đổi cả Gateway.
public static class CacheKeys
{
    public static string Session(Guid sessionFamilyId) => $"session:{sessionFamilyId}";

    public static string Permissions(Guid userId) => $"perm:{userId}";

    /// Hash email để key không chứa dữ liệu cá nhân.
    public static string LoginFailures(string normalizedEmail)
        => $"rl:email:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail)))}";

    public static string Generation(string key) => $"{key}:gen";
}
```

`Caching/RedisFailure.cs`:

```csharp
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

internal static class RedisFailure
{
    public static bool Is(Exception exception) => exception is RedisException or TimeoutException;
}
```

`Caching/GuardedCacheWrite.cs`:

```csharp
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

/// Cache không TTL có race: request A đọc DB (dữ liệu cũ) → admin đổi + xoá key → A ghi lại dữ liệu cũ, tồn tại mãi.
/// Chặn bằng bộ đếm thế hệ: mỗi lần xoá key tăng {key}:gen; bên ghi chỉ ghi nếu thế hệ không đổi
/// kể từ lúc nó đọc (đọc thế hệ TRƯỚC khi truy vấn DB).
public static class GuardedCacheWrite
{
    public static readonly TimeSpan GenerationLifetime = TimeSpan.FromDays(1);

    public static Task<RedisValue> ReadGenerationAsync(IDatabase db, string key)
        => db.StringGetAsync(CacheKeys.Generation(key));

    public static Task<bool> SetIfUnchangedAsync(IDatabase db, string key, string value, RedisValue generation, TimeSpan? expiry)
    {
        var generationKey = CacheKeys.Generation(key);
        var transaction = db.CreateTransaction();
        transaction.AddCondition(generation.IsNull
            ? Condition.KeyNotExists(generationKey)
            : Condition.StringEqual(generationKey, generation));
        _ = transaction.StringSetAsync(key, value, expiry);
        return transaction.ExecuteAsync();
    }

    public static async Task InvalidateAsync(IDatabase db, IReadOnlyCollection<string> keys)
    {
        var batch = db.CreateBatch();
        var pending = new List<Task>();
        foreach (var key in keys)
        {
            var generationKey = CacheKeys.Generation(key);
            pending.Add(batch.KeyDeleteAsync(key));
            pending.Add(batch.StringIncrementAsync(generationKey));
            pending.Add(batch.KeyExpireAsync(generationKey, GenerationLifetime));
        }
        batch.Execute();
        await Task.WhenAll(pending);
    }
}
```

`Caching/SessionCachePayload.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Infrastructure.Caching;

/// JSON Gateway đọc: {"userId":"...","sv":1,"absExp":1737000000}.
internal sealed record SessionCachePayload(
    [property: JsonPropertyName("userId")] Guid UserId,
    [property: JsonPropertyName("sv")] int SecurityVersion,
    [property: JsonPropertyName("absExp")] long AbsoluteExpiresAtUnix)
{
    public static string Serialize(Guid userId, int securityVersion, DateTimeOffset absoluteExpiresAtUtc)
        => JsonSerializer.Serialize(new SessionCachePayload(userId, securityVersion, absoluteExpiresAtUtc.ToUnixTimeSeconds()));

    public static string Serialize(SessionCacheEntry entry)
        => Serialize(entry.UserId, entry.SecurityVersion, entry.AbsoluteExpiresAtUtc);
}
```

`Caching/SessionCache.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

public sealed class SessionCache : ISessionCache
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionCache> _logger;

    public SessionCache(IConnectionMultiplexer redis, TimeProvider time, ILogger<SessionCache> logger)
    {
        _redis = redis;
        _time = time;
        _logger = logger;
    }

    public async Task SetAsync(SessionCacheEntry entry, CancellationToken ct = default)
    {
        var ttl = entry.AbsoluteExpiresAtUtc - _time.GetUtcNow();
        if (ttl <= TimeSpan.Zero) return;
        try
        {
            await _redis.GetDatabase().StringSetAsync(
                CacheKeys.Session(entry.SessionFamilyId), SessionCachePayload.Serialize(entry), ttl);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not cache session {SessionFamilyId}; gateway will fall back to the API", entry.SessionFamilyId);
        }
    }
}
```

`Caching/LoginRateLimiter.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

/// Chặn tạm theo email chuẩn hoá. Không đổi gì trong DB, nên kẻ xấu không khoá vĩnh viễn được tài khoản người khác.
public sealed class LoginRateLimiter : ILoginRateLimiter
{
    public const int MaxFailures = 5;
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<LoginRateLimiter> _logger;

    public LoginRateLimiter(IConnectionMultiplexer redis, ILogger<LoginRateLimiter> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = CacheKeys.LoginFailures(normalizedEmail);
            var count = await db.StringGetAsync(key);
            if (!count.HasValue || (long)count < MaxFailures) return null;
            return await db.KeyTimeToLiveAsync(key) ?? Window;
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Login rate limiter unavailable; allowing attempt");
            return null;
        }
    }

    public async Task RegisterFailureAsync(string normalizedEmail, CancellationToken ct = default)
    {
        try
        {
            var db = _redis.GetDatabase();
            var key = CacheKeys.LoginFailures(normalizedEmail);
            await db.StringIncrementAsync(key);
            // Luôn đảm bảo có TTL (kể cả khi lần INCR đầu bị gián đoạn trước EXPIRE) — không bao giờ khoá vĩnh viễn.
            await db.KeyExpireAsync(key, Window, ExpireWhen.HasNoExpiry);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Login rate limiter unavailable; failure not counted");
        }
    }

    public async Task ResetAsync(string normalizedEmail, CancellationToken ct = default)
    {
        try
        {
            await _redis.GetDatabase().KeyDeleteAsync(CacheKeys.LoginFailures(normalizedEmail));
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Login rate limiter unavailable; counter not reset");
        }
    }
}
```

`Caching/CacheInvalidator.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

public sealed class CacheInvalidator : ICacheInvalidator
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<CacheInvalidator> _logger;
    private readonly List<CacheInvalidation> _pending = new();

    public CacheInvalidator(AppDbContext db, IConnectionMultiplexer redis, TimeProvider time, ILogger<CacheInvalidator> logger)
    {
        _db = db;
        _redis = redis;
        _time = time;
        _logger = logger;
    }

    public void InvalidateSession(Guid sessionFamilyId) => Enqueue(CacheKeys.Session(sessionFamilyId));

    public void InvalidatePermissions(Guid userId) => Enqueue(CacheKeys.Permissions(userId));

    public async Task FlushAsync(CancellationToken ct = default)
    {
        if (_pending.Count == 0) return;
        var batch = _pending.ToList();
        _pending.Clear();
        try
        {
            await GuardedCacheWrite.InvalidateAsync(_redis.GetDatabase(), batch.Select(r => r.Key).ToList());
            _db.CacheInvalidations.RemoveRange(batch);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (RedisFailure.Is(ex) || ex is DbUpdateException)
        {
            _logger.LogWarning(ex, "Deferred {Count} cache invalidations to the background worker", batch.Count);
        }
    }

    private void Enqueue(string key)
    {
        if (_pending.Any(p => p.Key == key)) return;
        var row = CacheInvalidation.Create(key, _time.GetUtcNow());
        _db.CacheInvalidations.Add(row);
        _pending.Add(row);
    }
}
```

`Caching/CacheInvalidationProcessor.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Caching;

public sealed class CacheInvalidationProcessor
{
    private const int AlertAfterAttempts = 10;

    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<CacheInvalidationProcessor> _logger;

    public CacheInvalidationProcessor(AppDbContext db, IConnectionMultiplexer redis, ILogger<CacheInvalidationProcessor> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<int> ProcessPendingAsync(int batchSize, CancellationToken ct = default)
    {
        var rows = await _db.CacheInvalidations.OrderBy(r => r.CreatedAtUtc).Take(batchSize).ToListAsync(ct);
        if (rows.Count == 0) return 0;

        try
        {
            await GuardedCacheWrite.InvalidateAsync(_redis.GetDatabase(), rows.Select(r => r.Key).ToList());
            _db.CacheInvalidations.RemoveRange(rows);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            foreach (var row in rows) row.MarkFailed(ex.Message);
            var level = rows.Any(r => r.Attempts >= AlertAfterAttempts) ? LogLevel.Error : LogLevel.Warning;
            _logger.Log(level, ex, "Could not invalidate {Count} cache keys; will retry", rows.Count);
        }

        await _db.SaveChangesAsync(ct);
        return rows.Count;
    }
}
```

`Caching/CacheInvalidationWorker.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleanArchCqrs.Infrastructure.Caching;

public sealed class CacheInvalidationWorker : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<CacheInvalidationWorker> _logger;

    public CacheInvalidationWorker(IServiceScopeFactory scopes, ILogger<CacheInvalidationWorker> logger)
    {
        _scopes = scopes;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await using var scope = _scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<CacheInvalidationProcessor>()
                    .ProcessPendingAsync(BatchSize, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Cache invalidation worker iteration failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

DI — trong `AddInfrastructureServices` thêm (và `using CleanArchCqrs.Infrastructure.Caching;`):

```csharp
        services.AddSingleton<ISessionCache, SessionCache>();
        services.AddSingleton<ILoginRateLimiter, LoginRateLimiter>();
        services.AddScoped<ICacheInvalidator, CacheInvalidator>();
        services.AddScoped<CacheInvalidationProcessor>();
        services.AddHostedService<CacheInvalidationWorker>();
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter RedisServicesTests`
Expected: PASS 8/8.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(caching): add session cache, email rate limiter and outbox-style cache invalidation with generation guard"
```

---

### Task 3.3: Người dùng hiện tại + ghi audit

**Files:**
- Modify: `src/CleanArchCqrs.Application/Common/Interfaces/ICurrentUser.cs`, `src/CleanArchCqrs.API/Services/CurrentUser.cs`
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/IAuditRecorder.cs`, `src/CleanArchCqrs.Application/Common/Auditing/AuditActions.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Auditing/AuditRecorder.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Test: Create `tests/CleanArchCqrs.UnitTests/Infrastructure/Auditing/AuditRecorderTests.cs`, `tests/CleanArchCqrs.UnitTests/API/Services/CurrentUserTests.cs`; Modify fake `ICurrentUser` trong `AuditSaveChangesInterceptorTests.cs`, `InfrastructureServiceExtensionsTests.cs`

**Interfaces:**
- Consumes: `AuditRecord`, `AuditResult` (2.5), `IRequestContext` (2.5).
- Produces:
  - `ICurrentUser { Guid? UserId; Guid? SessionFamilyId; bool IsAuthenticated; }` (bỏ `Email`, `Role` — JWT không còn 2 claim này).
  - `IAuditRecorder.Record(string action, AuditResult result, string? reason = null, string? resourceType = null, string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)` — thêm `AuditRecord` vào DbContext hiện tại (lưu cùng `SaveChangesAsync` của caller). `actorId` mặc định = `ICurrentUser.UserId`. Metadata > 4096 byte ⇒ `ArgumentException`.
  - `AuditActions` hằng: `Login="auth.login"`, `RefreshReuse="auth.refresh.reuse"`, `Logout="auth.logout"`, `LogoutAll="auth.logout_all"`, `SessionRevoke="auth.session.revoke"`, `PasswordChange="auth.password.change"`, `RateLimited="auth.rate_limited"`, `AuthorizationDenied="authz.denied"`, `UserCreate="users.create"`, `UserActivate="users.activate"`, `UserDeactivate="users.deactivate"`, `UserRolesSet="users.roles.set"`, `UserPermissionGrant="users.permissions.grant"`, `UserPermissionRevoke="users.permissions.revoke"`, `RoleCreate="roles.create"`, `RoleUpdate="roles.update"`, `RolePermissionsSet="roles.permissions.set"`.

- [ ] **Step 1: Viết test (đỏ)**

`Infrastructure/Auditing/AuditRecorderTests.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Infrastructure.Auditing;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Auditing;

sealed class StubCurrentUser : ICurrentUser
{
    public Guid? UserId { get; init; }
    public Guid? SessionFamilyId => null;
    public bool IsAuthenticated => UserId is not null;
}

sealed class StubRequestContext : IRequestContext
{
    public string? CorrelationId => "corr-1";
    public string? IpAddress => "10.0.0.9";
    public string? UserAgent => "UA";
}

sealed class FixedTimeProvider : TimeProvider
{
    private readonly DateTimeOffset _now;
    public FixedTimeProvider(DateTimeOffset now) => _now = now;
    public override DateTimeOffset GetUtcNow() => _now;
}

public class AuditRecorderTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 8, 0, 0, TimeSpan.Zero);

    private static (AppDbContext Db, AuditRecorder Recorder) Create(Guid? currentUserId)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (db, new AuditRecorder(db, new StubCurrentUser { UserId = currentUserId }, new StubRequestContext(), new FixedTimeProvider(Now)));
    }

    [Fact]
    public async Task Record_FillsRequestContextAndDefaultsActorToCurrentUser()
    {
        var current = Guid.NewGuid();
        var (db, recorder) = Create(current);

        recorder.Record("auth.logout", AuditResult.Succeeded, resourceType: "SessionFamily", resourceId: "f-1");
        await db.SaveChangesAsync();

        var record = Assert.Single(db.AuditRecords);
        Assert.Equal(current, record.ActorId);
        Assert.Equal("corr-1", record.CorrelationId);
        Assert.Equal("10.0.0.9", record.IpAddress);
        Assert.Equal(Now, record.TimestampUtc);
    }

    [Fact]
    public async Task Record_ExplicitActorAndMetadata()
    {
        var actor = Guid.NewGuid();
        var (db, recorder) = Create(currentUserId: null);

        recorder.Record("auth.login", AuditResult.Failed, "InvalidPassword", actorId: actor,
            metadata: new Dictionary<string, object?> { ["count"] = 2 });
        await db.SaveChangesAsync();

        var record = Assert.Single(db.AuditRecords);
        Assert.Equal(actor, record.ActorId);
        Assert.Equal("{\"count\":2}", record.Metadata);
    }

    [Fact]
    public void Record_OversizedMetadata_Throws()
    {
        var (_, recorder) = Create(null);

        Assert.Throws<ArgumentException>(() => recorder.Record("x.y", AuditResult.Succeeded,
            metadata: new Dictionary<string, object?> { ["blob"] = new string('a', 5000) }));
    }
}
```

`API/Services/CurrentUserTests.cs`:

```csharp
using System.Security.Claims;
using CleanArchCqrs.API.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Services;

public class CurrentUserTests
{
    [Fact]
    public void ReadsSubAndFidClaims()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", userId.ToString()), new Claim("fid", familyId.ToString())], "Bearer"))
        };

        var current = new CurrentUser(new HttpContextAccessor { HttpContext = http });

        Assert.Equal(userId, current.UserId);
        Assert.Equal(familyId, current.SessionFamilyId);
        Assert.True(current.IsAuthenticated);
    }

    [Fact]
    public void Anonymous_ReturnsNulls()
    {
        var current = new CurrentUser(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.Null(current.UserId);
        Assert.Null(current.SessionFamilyId);
        Assert.False(current.IsAuthenticated);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "AuditRecorderTests|CurrentUserTests"`
Expected: FAIL biên dịch.

- [ ] **Step 3: `ICurrentUser` mới**

`ICurrentUser.cs` (toàn bộ):

```csharp
namespace CleanArchCqrs.Application.Common.Interfaces
{
    //⚠ Application cần biết "ai đang thao tác" nhưng **không được** `using Microsoft.AspNetCore.Http`.
    //JWT chỉ mang định danh (sub, fid, sv) — role/permission lấy qua IPermissionService, không qua đây.
    public interface ICurrentUser
    {
        Guid? UserId { get; }
        Guid? SessionFamilyId { get; }
        bool IsAuthenticated { get; }
    }
}
```

`API/Services/CurrentUser.cs` (toàn bộ):

```csharp
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.API.Services;

public sealed class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUser(IHttpContextAccessor httpContextAccessor) => _httpContextAccessor = httpContextAccessor;

    public Guid? UserId => GuidClaim("sub");

    public Guid? SessionFamilyId => GuidClaim("fid");

    public bool IsAuthenticated => _httpContextAccessor.HttpContext?.User?.Identity?.IsAuthenticated ?? false;

    private Guid? GuidClaim(string type)
        => Guid.TryParse(_httpContextAccessor.HttpContext?.User?.FindFirst(type)?.Value, out var id) ? id : null;
}
```

Sửa 2 fake `ICurrentUser` trong unit test (`AuditSaveChangesInterceptorTests.FakeCurrentUser`, `InfrastructureServiceExtensionsTests.FakeCurrentUser`): xoá `Email`, `Role`; thêm `public Guid? SessionFamilyId => null;`.

- [ ] **Step 4: Recorder**

`Application/Common/Auditing/AuditActions.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Auditing;

public static class AuditActions
{
    public const string Login = "auth.login";
    public const string RefreshReuse = "auth.refresh.reuse";
    public const string Logout = "auth.logout";
    public const string LogoutAll = "auth.logout_all";
    public const string SessionRevoke = "auth.session.revoke";
    public const string PasswordChange = "auth.password.change";
    public const string RateLimited = "auth.rate_limited";
    public const string AuthorizationDenied = "authz.denied";
    public const string UserCreate = "users.create";
    public const string UserActivate = "users.activate";
    public const string UserDeactivate = "users.deactivate";
    public const string UserRolesSet = "users.roles.set";
    public const string UserPermissionGrant = "users.permissions.grant";
    public const string UserPermissionRevoke = "users.permissions.revoke";
    public const string RoleCreate = "roles.create";
    public const string RoleUpdate = "roles.update";
    public const string RolePermissionsSet = "roles.permissions.set";
}
```

`Application/Common/Interfaces/IAuditRecorder.cs`:

```csharp
using CleanArchCqrs.Domain.Common.Auditing;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Ghi AuditRecord vào unit of work hiện tại. Caller tự SaveChangesAsync —
/// nhánh thất bại cần lưu vết (401 login, 429, reuse) phải save TRƯỚC khi ném lỗi.
public interface IAuditRecorder
{
    void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null);
}
```

`Infrastructure/Auditing/AuditRecorder.cs`:

```csharp
using System.Text;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Infrastructure.Persistence;

namespace CleanArchCqrs.Infrastructure.Auditing;

public sealed class AuditRecorder : IAuditRecorder
{
    private const int MaxMetadataBytes = 4096;

    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _request;
    private readonly TimeProvider _time;

    public AuditRecorder(AppDbContext db, ICurrentUser currentUser, IRequestContext request, TimeProvider time)
    {
        _db = db;
        _currentUser = currentUser;
        _request = request;
        _time = time;
    }

    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)
    {
        var metadataJson = metadata is null ? null : JsonSerializer.Serialize(metadata);
        if (metadataJson is not null && Encoding.UTF8.GetByteCount(metadataJson) > MaxMetadataBytes)
            throw new ArgumentException($"Audit metadata exceeds {MaxMetadataBytes} bytes.", nameof(metadata));

        _db.AuditRecords.Add(AuditRecord.Create(
            actorId ?? _currentUser.UserId, action, result, reason, resourceType, resourceId,
            _request.CorrelationId, _request.IpAddress, _request.UserAgent, metadataJson, _time.GetUtcNow()));
    }
}
```

DI — thêm: `services.AddScoped<IAuditRecorder, AuditRecorder>();` (và `using CleanArchCqrs.Infrastructure.Auditing;`).

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet build && dotnet test tests/CleanArchCqrs.UnitTests`
Expected: 0 warning; PASS toàn bộ.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.UnitTests
git commit -m "feat(audit): add audit recorder and session-aware current user"
```

---

### Task 3.4: Login end-to-end + xác thực JWT ở API

**Files:**
- Create: `src/CleanArchCqrs.Application/Auth/AuthMessages.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Models/AuthSession.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Commands/Login/LoginCommand.cs`, `LoginCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Validators/LoginCommandValidator.cs`
- Create: `src/CleanArchCqrs.API/Auth/AuthCookieWriter.cs`
- Create: `src/CleanArchCqrs.API/Contracts/Auth/AccessTokenResponse.cs`
- Create: `src/CleanArchCqrs.API/Controllers/AuthController.cs`
- Create: `src/CleanArchCqrs.API/DependencyInjection/ApiAuthenticationExtensions.cs`
- Create: `src/CleanArchCqrs.API/Middleware/UserLogContextMiddleware.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs` (viết lại toàn bộ)
- Test: Create `tests/CleanArchCqrs.IntegrationTests/Helpers/AuthTestClient.cs`, `Helpers/TestData.cs`, `Auth/LoginTests.cs`

**Interfaces:**
- Consumes: `IUserRepository.GetByEmailAsync`, `ISessionRepository.AddAsync`, `SessionFamily.Start`, `IPasswordHasher.Verify/SimulateVerify`, `ITokenService`, `IRefreshTokenGenerator`, `ILoginRateLimiter`, `ISessionCache`, `IAuditRecorder`, `IRequestContext`, `IUnitOfWork`, `TimeProvider`, `ICsrfTokenService`, `JwtOptions`.
- Produces:
  - `AuthMessages.LoginFailed = "Email hoặc mật khẩu không đúng."`, `SessionInvalid = "Phiên đăng nhập không còn hiệu lực."`, `TooManyAttempts = "Bạn đã thử quá nhiều lần. Vui lòng thử lại sau."`.
  - `record AuthSession(string AccessToken, DateTimeOffset AccessTokenExpiresAtUtc, bool MustChangePassword, string RefreshToken, Guid SessionFamilyId, DateTimeOffset SessionExpiresAtUtc)`.
  - `record LoginCommand(string Email, string Password) : IRequest<AuthSession>`.
  - `AuthCookieWriter` (scoped): hằng `RefreshCookie = "__Host-rt"`, `CsrfCookie = "__Host-csrf"`; `void Write(HttpResponse response, string refreshToken, Guid sessionFamilyId, DateTimeOffset sessionExpiresAtUtc)`; `void Clear(HttpResponse response)`.
  - `record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword)`.
  - `AuthController` (`[Route("api/v1/auth")]`, `[Authorize]` cấp class): `POST login` (`[AllowAnonymous]`).
  - `IServiceCollection.AddApiAuthentication()` — JwtBearer HS256, `ClockSkew` 30s, `MapInboundClaims=false`, 401 dạng Problem Details.
  - Test helper: `AuthTestClient` (giữ access token + cookie tay), `TestData` (tạo user thẳng vào DB, truy vấn DB).

- [ ] **Step 1: Test helper**

`Helpers/TestData.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.IntegrationTests.Helpers;

public static class TestData
{
    public const string DefaultPassword = "Test-Password-123";

    public static string NewEmail(string prefix = "user") => $"{prefix}-{Guid.NewGuid():N}@test.local";

    /// Tạo user thẳng vào DB (không qua API). mustChangePassword=false giả lập user đã đổi mật khẩu lần đầu.
    public static async Task<Guid> CreateUserAsync(ApiFactory factory, string email, string password = DefaultPassword,
        bool mustChangePassword = false, bool isActive = true, params string[] roleCodes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();

        var user = User.Create("Test User", email, hasher.Hash(password), null);
        if (!mustChangePassword) user.ChangePassword(hasher.Hash(password));
        if (roleCodes.Length > 0)
        {
            var roleIds = await db.Roles.Where(r => roleCodes.Contains(r.Code)).Select(r => r.Id).ToListAsync();
            user.SetRoles(roleIds, null, DateTimeOffset.UtcNow);
        }
        if (!isActive) user.Deactivate();

        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    public static async Task<T> QueryAsync<T>(ApiFactory factory, Func<AppDbContext, Task<T>> query)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }
}
```

`Helpers/AuthTestClient.cs`:

```csharp
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
```

- [ ] **Step 2: Viết test login (đỏ)**

`Auth/LoginTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class LoginTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public LoginTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private AuthTestClient NewClient() => new(_factory.CreateHttpsClient());

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task ValidCredentials_ReturnAccessTokenAndHardenedCookies()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = NewClient();

        var response = await client.LoginAsync(email, TestData.DefaultPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False((await BodyAsync(response)).GetProperty("mustChangePassword").GetBoolean());

        var cookies = response.Headers.GetValues("Set-Cookie").ToList();
        var refresh = cookies.Single(c => c.StartsWith("__Host-rt=")).ToLowerInvariant();
        Assert.Contains("httponly", refresh);
        Assert.Contains("secure", refresh);
        Assert.Contains("samesite=strict", refresh);
        Assert.Contains("path=/", refresh);
        Assert.DoesNotContain("domain=", refresh);
        Assert.Contains("max-age=", refresh);
        Assert.DoesNotContain("httponly", cookies.Single(c => c.StartsWith("__Host-csrf=")).ToLowerInvariant());

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken);
        Assert.Equal(
            new[] { "aud", "exp", "fid", "iat", "iss", "jti", "nbf", "sub", "sv" },
            jwt.Claims.Select(c => c.Type).Distinct().OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public async Task WrongPassword_UnknownEmail_InactiveAccount_AllLookIdentical()
    {
        var active = TestData.NewEmail();
        var inactive = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, active);
        await TestData.CreateUserAsync(_factory, inactive, isActive: false);

        var titles = new List<string?>();
        foreach (var (email, password) in new[]
                 {
                     (active, "Wrong-Password-1"),
                     (TestData.NewEmail(), TestData.DefaultPassword),
                     (inactive, TestData.DefaultPassword),
                 })
        {
            var response = await NewClient().LoginAsync(email, password);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            var body = await BodyAsync(response);
            Assert.Equal("unauthenticated", body.GetProperty("code").GetString());
            titles.Add(body.GetProperty("title").GetString());
        }

        Assert.Equal("Email hoặc mật khẩu không đúng.", Assert.Single(titles.Distinct()));
    }

    [Fact]
    public async Task Failure_IsAuditedWithReasonAndActor()
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email);

        await NewClient().LoginAsync(email, "Wrong-Password-1");

        var record = await TestData.QueryAsync(_factory, db => db.AuditRecords
            .SingleAsync(a => a.Action == AuditActions.Login && a.ActorId == userId));
        Assert.Equal(AuditResult.Failed, record.Result);
        Assert.Equal("InvalidPassword", record.Reason);
    }

    [Fact]
    public async Task FiveFailures_SixthAttemptIsRateLimited()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);

        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await NewClient().LoginAsync(email, "Wrong-Password-1")).StatusCode);

        var response = await NewClient().LoginAsync(email, TestData.DefaultPassword);   // kể cả đúng mật khẩu

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.True(int.Parse(response.Headers.GetValues("Retry-After").Single()) > 0);
        Assert.Equal("rate_limited", (await BodyAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task EmptyFields_Return400WithFieldErrors()
    {
        var response = await NewClient().LoginAsync("", "");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.True(body.GetProperty("errors").TryGetProperty("email", out _));
        Assert.True(body.GetProperty("errors").TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Success_CachesSessionForGatewayAndAudits()
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email);
        var client = NewClient();

        await client.LoginAsync(email, TestData.DefaultPassword);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken);
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        using var cached = JsonDocument.Parse((string)(await redis.StringGetAsync(CacheKeys.Session(Guid.Parse(jwt.GetClaim("fid").Value))))!);
        Assert.Equal(int.Parse(jwt.GetClaim("sv").Value), cached.RootElement.GetProperty("sv").GetInt32());
        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.Login && a.ActorId == userId && a.Result == AuditResult.Succeeded)));
    }
}
```

- [ ] **Step 3: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter LoginTests`
Expected: FAIL (biên dịch — `AuditActions`… đã có, nhưng endpoint 404; sau khi build được: 404 Not Found).

- [ ] **Step 4: Application — use case Login**

`Auth/AuthMessages.cs`:

```csharp
namespace CleanArchCqrs.Application.Auth;

public static class AuthMessages
{
    /// Giống hệt cho email không tồn tại / sai mật khẩu / tài khoản bị khoá — chống dò email.
    public const string LoginFailed = "Email hoặc mật khẩu không đúng.";
    public const string SessionInvalid = "Phiên đăng nhập không còn hiệu lực.";
    public const string TooManyAttempts = "Bạn đã thử quá nhiều lần. Vui lòng thử lại sau.";
}
```

`Auth/Models/AuthSession.cs`:

```csharp
namespace CleanArchCqrs.Application.Auth.Models;

/// Kết quả login/refresh. RefreshToken chỉ đi vào cookie HttpOnly, không bao giờ vào body response.
public sealed record AuthSession(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAtUtc,
    bool MustChangePassword,
    string RefreshToken,
    Guid SessionFamilyId,
    DateTimeOffset SessionExpiresAtUtc);
```

`Auth/Commands/Login/LoginCommand.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Login;

public sealed record LoginCommand(string Email, string Password) : IRequest<AuthSession>;
```

`Auth/Validators/LoginCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Commands.Login;
using FluentValidation;

namespace CleanArchCqrs.Application.Auth.Validators;

/// Không kiểm độ mạnh mật khẩu ở login — quy tắc đó thuộc về đặt/đổi mật khẩu.
public sealed class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
```

`Auth/Commands/Login/LoginCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Login;

public sealed class LoginCommandHandler : IRequestHandler<LoginCommand, AuthSession>
{
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ILoginRateLimiter _rateLimiter;
    private readonly ISessionCache _sessionCache;
    private readonly IAuditRecorder _audit;
    private readonly IRequestContext _request;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public LoginCommandHandler(IUserRepository users, ISessionRepository sessions, IPasswordHasher passwordHasher,
        ITokenService tokenService, IRefreshTokenGenerator refreshTokens, ILoginRateLimiter rateLimiter,
        ISessionCache sessionCache, IAuditRecorder audit, IRequestContext request, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _users = users;
        _sessions = sessions;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _refreshTokens = refreshTokens;
        _rateLimiter = rateLimiter;
        _sessionCache = sessionCache;
        _audit = audit;
        _request = request;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<AuthSession> Handle(LoginCommand request, CancellationToken ct)
    {
        var email = User.NormalizeEmail(request.Email);

        var lockout = await _rateLimiter.GetLockoutRemainingAsync(email, ct);
        if (lockout is not null)
        {
            _audit.Record(AuditActions.RateLimited, AuditResult.Denied, reason: "EmailLockout");
            await _unitOfWork.SaveChangesAsync(ct);
            throw new TooManyRequestsException(lockout.Value, AuthMessages.TooManyAttempts);
        }

        var user = await _users.GetByEmailAsync(email, ct);
        var failure = FindFailure(user, request.Password);
        if (user is null || failure is not null)
        {
            await _rateLimiter.RegisterFailureAsync(email, ct);
            _audit.Record(AuditActions.Login, AuditResult.Failed, reason: failure,
                resourceType: nameof(User), resourceId: user?.Id.ToString(), actorId: user?.Id);
            await _unitOfWork.SaveChangesAsync(ct);   // lưu vết TRƯỚC khi ném
            throw new UnauthorizedException(AuthMessages.LoginFailed);
        }

        await _rateLimiter.ResetAsync(email, ct);
        var refresh = _refreshTokens.Generate();
        var family = SessionFamily.Start(user.Id, refresh.Hash, _time.GetUtcNow(), _request.IpAddress, _request.UserAgent);
        user.RecordLogin();
        await _sessions.AddAsync(family, ct);
        _audit.Record(AuditActions.Login, AuditResult.Succeeded,
            resourceType: nameof(SessionFamily), resourceId: family.Id.ToString(), actorId: user.Id);
        await _unitOfWork.SaveChangesAsync(ct);   // một lần commit cho RecordLogin + family + audit

        await _sessionCache.SetAsync(
            new SessionCacheEntry(family.Id, user.Id, user.SecurityVersion, family.AbsoluteExpiresAtUtc), ct);

        var access = _tokenService.CreateAccessToken(user.Id, family.Id, user.SecurityVersion);
        return new AuthSession(access.Token, access.ExpiresAtUtc, user.MustChangePassword,
            refresh.Token, family.Id, family.AbsoluteExpiresAtUtc);
    }

    private string? FindFailure(User? user, string password)
    {
        if (user is null)
        {
            _passwordHasher.SimulateVerify(password);   // cân thời gian với nhánh sai mật khẩu
            return "EmailNotFound";
        }
        if (!_passwordHasher.Verify(password, user.PasswordHash)) return "InvalidPassword";
        if (!user.IsActive) return "AccountInactive";
        return null;
    }
}
```

- [ ] **Step 5: API — cookie, controller, JwtBearer, log context**

`Auth/AuthCookieWriter.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;

namespace CleanArchCqrs.API.Auth;

/// Đặc tả kỹ thuật §4.1: host-only, Secure, SameSite=Strict, Path=/, không Domain (điều kiện của tiền tố __Host-).
public sealed class AuthCookieWriter
{
    public const string RefreshCookie = "__Host-rt";
    public const string CsrfCookie = "__Host-csrf";

    private readonly ICsrfTokenService _csrf;
    private readonly TimeProvider _time;

    public AuthCookieWriter(ICsrfTokenService csrf, TimeProvider time)
    {
        _csrf = csrf;
        _time = time;
    }

    public void Write(HttpResponse response, string refreshToken, Guid sessionFamilyId, DateTimeOffset sessionExpiresAtUtc)
    {
        var maxAge = sessionExpiresAtUtc - _time.GetUtcNow();
        response.Cookies.Append(RefreshCookie, refreshToken, Options(httpOnly: true, maxAge));
        // Không HttpOnly: JS đọc để gửi lại trong header X-CSRF-Token.
        response.Cookies.Append(CsrfCookie, _csrf.Create(sessionFamilyId), Options(httpOnly: false, maxAge));
    }

    public void Clear(HttpResponse response)
    {
        response.Cookies.Delete(RefreshCookie, Options(httpOnly: true, maxAge: null));
        response.Cookies.Delete(CsrfCookie, Options(httpOnly: false, maxAge: null));
    }

    private static CookieOptions Options(bool httpOnly, TimeSpan? maxAge) => new()
    {
        HttpOnly = httpOnly,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/",
        MaxAge = maxAge,
        IsEssential = true,
    };
}
```

`Contracts/Auth/AccessTokenResponse.cs`:

```csharp
namespace CleanArchCqrs.API.Contracts.Auth;

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword);
```

`Controllers/AuthController.cs`:

```csharp
using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Contracts.Auth;
using CleanArchCqrs.Application.Auth.Commands.Login;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
[Authorize]
public sealed class AuthController : ControllerBase
{
    private readonly ISender _mediator;
    private readonly AuthCookieWriter _cookies;

    public AuthController(ISender mediator, AuthCookieWriter cookies)
    {
        _mediator = mediator;
        _cookies = cookies;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AccessTokenResponse>> Login(LoginCommand command, CancellationToken ct)
    {
        var session = await _mediator.Send(command, ct);
        _cookies.Write(Response, session.RefreshToken, session.SessionFamilyId, session.SessionExpiresAtUtc);
        return Ok(new AccessTokenResponse(session.AccessToken, session.AccessTokenExpiresAtUtc, session.MustChangePassword));
    }
}
```

`DependencyInjection/ApiAuthenticationExtensions.cs`:

```csharp
using System.Text;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchCqrs.API.DependencyInjection;

public static class ApiAuthenticationExtensions
{
    /// API validate lại JWT (chữ ký, alg, iss, aud, exp, nbf) làm lớp phòng thủ thứ hai sau Gateway.
    /// Không tra phiên ở đây — Gateway đã làm; API chỉ tin chữ ký.
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((options, jwtOptions) =>
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
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();
                        await ProblemResponseWriter.WriteAsync(context.HttpContext, StatusCodes.Status401Unauthorized,
                            ErrorCodes.Unauthenticated, "Chưa đăng nhập hoặc phiên đã hết hạn.");
                    }
                };
            });
        return services;
    }
}
```

`Middleware/UserLogContextMiddleware.cs`:

```csharp
using Serilog.Context;

namespace CleanArchCqrs.API.Middleware;

/// Gắn UserId/SessionFamilyId vào mọi dòng log trong request đã xác thực. Đặt SAU UseAuthentication.
public sealed class UserLogContextMiddleware
{
    private readonly RequestDelegate _next;

    public UserLogContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            await _next(context);
            return;
        }

        using (LogContext.PushProperty("UserId", context.User.FindFirst("sub")?.Value))
        using (LogContext.PushProperty("SessionFamilyId", context.User.FindFirst("fid")?.Value))
        {
            await _next(context);
        }
    }
}
```

`Program.cs` (toàn bộ — gom mọi thay đổi Giai đoạn 1–3):

```csharp
using System.Net;
using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.DependencyInjection;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.API.Logging;
using CleanArchCqrs.API.Middleware;
using CleanArchCqrs.API.Services;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.DependencyInjection;
using CleanArchCqrs.Infrastructure.DependencyInjection;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Models;
using Serilog;

namespace CleanArchCqrs.API;

/// <summary>
/// Application startup - wires up all layers, middleware, and Swagger.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Destructure.With<SensitiveDataDestructuringPolicy>());

        builder.Services.AddControllers()
            .AddJsonOptions(options =>
            {
                options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
                options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
            });
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
                ProblemResponseWriter.ToResult(context.HttpContext, 400, ErrorCodes.ValidationFailed, "Dữ liệu không hợp lệ.",
                    context.ModelState.Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray())));

        // Chỉ tin X-Forwarded-* từ proxy đã khai báo (mặc định: loopback). Production: thêm IP Gateway vào cấu hình.
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [])
                options.KnownProxies.Add(IPAddress.Parse(proxy));
        });

        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Clean Architecture CQRS Starter",
                Version = "v1",
                Description = "Starter template for Clean Architecture with CQRS and MediatR in ASP.NET Core 10"
            });
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ICurrentUser, CurrentUser>();
        builder.Services.AddScoped<IRequestContext, HttpRequestContext>();
        builder.Services.AddScoped<AuthCookieWriter>();
        builder.Services.AddApiAuthentication();

        builder.Services.AddApplicationServices();
        builder.Services.AddInfrastructureServices(builder.Configuration);

        var app = builder.Build();
        await DbInitializer.InitializeAsync(app.Services);

        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "CleanArchCqrs API v1");
                c.RoutePrefix = string.Empty; // Swagger at root
            });
        }

        app.UseForwardedHeaders();
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseExceptionHandler();
        app.UseSerilogRequestLogging(options => options.EnrichDiagnosticContext = (diagnostics, http) =>
        {
            if (http.User.Identity?.IsAuthenticated != true) return;
            diagnostics.Set("UserId", http.User.FindFirst("sub")?.Value);
            diagnostics.Set("SessionFamilyId", http.User.FindFirst("fid")?.Value);
        });

        app.UseHttpsRedirection();
        app.UseAuthentication();
        app.UseMiddleware<UserLogContextMiddleware>();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks("/health");

        await app.RunAsync();
    }
}
```

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter LoginTests`
Expected: PASS 6/6.

Run: `dotnet build && dotnet test`
Expected: 0 warning; toàn bộ xanh.

- [ ] **Step 7: Commit**

```bash
git add -A src tests
git commit -m "feat(auth): login issues access token, rotating refresh cookie and CSRF cookie; API validates JWT"
```

---

### Task 3.5: Refresh (rotation + strict reuse), Logout, lớp chặn CSRF

**Files:**
- Create: `src/CleanArchCqrs.Application/Auth/Commands/Refresh/RefreshCommand.cs`, `RefreshCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Commands/Logout/LogoutCommand.cs`, `LogoutCommandHandler.cs`
- Create: `src/CleanArchCqrs.API/Auth/CsrfProtectionFilter.cs`, `CsrfProtectedAttribute.cs`
- Modify: `src/CleanArchCqrs.API/Controllers/AuthController.cs`
- Test: Create `tests/CleanArchCqrs.IntegrationTests/Auth/RefreshAndLogoutTests.cs`; thêm package `Microsoft.Extensions.TimeProvider.Testing` cho project integration test

**Interfaces:**
- Consumes: `ISessionRepository.FindFamilyIdByTokenHashAsync/GetForUpdateAsync`, `SessionFamily.Rotate/Revoke`, `IUnitOfWork.BeginTransactionAsync`, `ICacheInvalidator`, `IAuditRecorder`, `AuthCookieWriter`, `ICsrfTokenService`, `AuthOptions.AllowedOrigins`.
- Produces:
  - `record RefreshCommand(string RefreshToken) : IRequest<AuthSession>` — ném `UnauthorizedException(AuthMessages.SessionInvalid)` cho mọi trường hợp không hợp lệ; reuse ⇒ revoke family + audit + commit **trước** khi ném.
  - `record LogoutCommand(string? RefreshToken) : IRequest` — idempotent.
  - `[CsrfProtected]` (action filter): Origin phải thuộc `Auth:AllowedOrigins`; nếu xác định được family (claim `fid`, hoặc tra hash cookie `__Host-rt`) thì `X-CSRF-Token` phải hợp lệ ⇒ nếu không: 403 `csrf_failed`.
  - `AuthController`: `POST refresh` và `POST logout` (`[AllowAnonymous]`, `[CsrfProtected]`); refresh 401 ⇒ xoá cookie.

- [ ] **Step 1: Viết test (đỏ)**

```bash
dotnet add tests/CleanArchCqrs.IntegrationTests package Microsoft.Extensions.TimeProvider.Testing
```

`Auth/RefreshAndLogoutTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class RefreshAndLogoutTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private ApiFactory _factory = default!;

    public RefreshAndLogoutTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
        => _factory = await ApiFactory.CreateAsync(_containers, configureServices: s => s.AddSingleton<TimeProvider>(_time));

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<AuthTestClient> LoggedInAsync()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    private static Guid FamilyOf(AuthTestClient client)
        => Guid.Parse(new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken).GetClaim("fid").Value);

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task Refresh_RotatesCookieAndKeepsSameSession()
    {
        var client = await LoggedInAsync();
        var oldRefresh = client.RefreshToken;
        var family = FamilyOf(client);

        var response = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotEqual(oldRefresh, client.RefreshToken);
        Assert.Equal(family, FamilyOf(client));
    }

    [Fact]
    public async Task Refresh_ReplayedOldToken_RevokesWholeFamily()
    {
        var client = await LoggedInAsync();
        var family = FamilyOf(client);
        var stolen = client.RefreshToken;
        (await client.RefreshAsync()).EnsureSuccessStatusCode();
        var legit = client.RefreshToken;

        client.RefreshToken = stolen;
        var replay = await client.RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, replay.StatusCode);
        Assert.Null(client.RefreshToken);   // cookie đã bị xoá
        client.RefreshToken = legit;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.RefreshReuse && a.ResourceId == family.ToString())));
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(family)));
    }

    [Fact]
    public async Task Refresh_TwoConcurrentWithSameToken_ExactlyOneSucceeds_FamilyRevoked()
    {
        var first = await LoggedInAsync();
        var second = first.CloneWith(_factory.CreateHttpsClient());

        var responses = await Task.WhenAll(first.RefreshAsync(), second.RefreshAsync());

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Unauthorized);
        var winner = responses[0].StatusCode == HttpStatusCode.OK ? first : second;
        Assert.Equal(HttpStatusCode.Unauthorized, (await winner.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Refresh_MissingCsrfHeader_Returns403()
    {
        var client = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", csrf: false, bearer: false);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("csrf_failed", await CodeAsync(response));
    }

    [Fact]
    public async Task Refresh_ForeignOrigin_Returns403()
    {
        var client = await LoggedInAsync();

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/refresh", bearer: false, origin: "https://evil.example");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_NoCookie_Returns401()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient()).RefreshAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_AfterSevenDays_Returns401EvenIfActive()
    {
        var client = await LoggedInAsync();
        _time.Advance(TimeSpan.FromDays(7).Add(TimeSpan.FromMinutes(1)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesSessionClearsCookiesAndCache()
    {
        var client = await LoggedInAsync();
        var family = FamilyOf(client);
        var refresh = client.RefreshToken;

        var response = await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(client.RefreshToken);
        Assert.Null(client.CsrfToken);
        client.RefreshToken = refresh;
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.RefreshAsync()).StatusCode);
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(family)));
    }

    [Fact]
    public async Task Logout_WithoutCookie_IsStill204()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient())
            .SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter RefreshAndLogoutTests`
Expected: FAIL — endpoint chưa có (404).

- [ ] **Step 3: Application**

`Auth/Commands/Refresh/RefreshCommand.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Refresh;

public sealed record RefreshCommand(string RefreshToken) : IRequest<AuthSession>;
```

`Auth/Commands/Refresh/RefreshCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Refresh;

/// Đặc tả kỹ thuật §4.2: trong transaction, khoá family → token, xoay vòng hoặc phát hiện reuse.
public sealed class RefreshCommandHandler : IRequestHandler<RefreshCommand, AuthSession>
{
    private readonly ISessionRepository _sessions;
    private readonly IUserRepository _users;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ITokenService _tokenService;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public RefreshCommandHandler(ISessionRepository sessions, IUserRepository users, IRefreshTokenGenerator refreshTokens,
        ITokenService tokenService, ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork,
        TimeProvider time)
    {
        _sessions = sessions;
        _users = users;
        _refreshTokens = refreshTokens;
        _tokenService = tokenService;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<AuthSession> Handle(RefreshCommand request, CancellationToken ct)
    {
        var presentedHash = _refreshTokens.Hash(request.RefreshToken);
        var now = _time.GetUtcNow();

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var familyId = await _sessions.FindFamilyIdByTokenHashAsync(presentedHash, ct)
            ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var family = await _sessions.GetForUpdateAsync(familyId, presentedHash, ct)
            ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var user = await _users.GetUserByIdAsync(family.UserId, ct);
        if (!user.IsActive) throw new UnauthorizedException(AuthMessages.SessionInvalid);

        var next = _refreshTokens.Generate();
        var result = family.Rotate(presentedHash, next.Hash, now);

        if (result == RotationResult.ReuseDetected)
        {
            _audit.Record(AuditActions.RefreshReuse, AuditResult.Denied,
                resourceType: nameof(SessionFamily), resourceId: family.Id.ToString(), actorId: family.UserId);
            _cacheInvalidator.InvalidateSession(family.Id);
            await _unitOfWork.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);   // commit việc thu hồi TRƯỚC khi trả 401 — không để exception rollback nó
            await _cacheInvalidator.FlushAsync(ct);
            throw new UnauthorizedException(AuthMessages.SessionInvalid);
        }

        if (result != RotationResult.Rotated)
            throw new UnauthorizedException(AuthMessages.SessionInvalid);

        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);   // commit rồi mới phát token

        var access = _tokenService.CreateAccessToken(user.Id, family.Id, user.SecurityVersion);
        return new AuthSession(access.Token, access.ExpiresAtUtc, user.MustChangePassword,
            next.Token, family.Id, family.AbsoluteExpiresAtUtc);
    }
}
```

`Auth/Commands/Logout/LogoutCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Logout;

public sealed record LogoutCommand(string? RefreshToken) : IRequest;
```

`Auth/Commands/Logout/LogoutCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.Logout;

/// Idempotent: cookie thiếu/không hợp lệ/phiên đã chết đều coi như đã logout.
public sealed class LogoutCommandHandler : IRequestHandler<LogoutCommand>
{
    private readonly ISessionRepository _sessions;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public LogoutCommandHandler(ISessionRepository sessions, IRefreshTokenGenerator refreshTokens,
        ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _sessions = sessions;
        _refreshTokens = refreshTokens;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(request.RefreshToken)) return;
        var hash = _refreshTokens.Hash(request.RefreshToken);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var familyId = await _sessions.FindFamilyIdByTokenHashAsync(hash, ct);
        if (familyId is null) return;
        var family = await _sessions.GetForUpdateAsync(familyId.Value, hash, ct);
        if (family is null || family.Status != SessionStatus.Active) return;

        family.Revoke(SessionRevokeReason.Logout, _time.GetUtcNow());
        _audit.Record(AuditActions.Logout, AuditResult.Succeeded,
            resourceType: nameof(SessionFamily), resourceId: family.Id.ToString(), actorId: family.UserId);
        _cacheInvalidator.InvalidateSession(family.Id);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

- [ ] **Step 4: API — CSRF filter và endpoint**

`Auth/CsrfProtectionFilter.cs`:

```csharp
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Auth;

/// Đặc tả kỹ thuật §4.1: kiểm Origin theo allowlist + CSRF token có chữ ký gắn phiên (cộng thêm SameSite=Strict của cookie).
public sealed class CsrfProtectionFilter : IAsyncActionFilter
{
    public const string HeaderName = "X-CSRF-Token";

    private readonly ICsrfTokenService _csrf;
    private readonly IRefreshTokenGenerator _refreshTokens;
    private readonly ISessionRepository _sessions;
    private readonly AuthOptions _options;

    public CsrfProtectionFilter(ICsrfTokenService csrf, IRefreshTokenGenerator refreshTokens,
        ISessionRepository sessions, IOptions<AuthOptions> options)
    {
        _csrf = csrf;
        _refreshTokens = refreshTokens;
        _sessions = sessions;
        _options = options.Value;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var http = context.HttpContext;
        var origin = http.Request.Headers.Origin.ToString();
        if (!_options.AllowedOrigins.Contains(origin, StringComparer.OrdinalIgnoreCase))
        {
            context.Result = Reject(http);
            return;
        }

        var familyId = await ResolveFamilyIdAsync(http);
        if (familyId is not null && !_csrf.IsValid(familyId.Value, http.Request.Headers[HeaderName].ToString()))
        {
            context.Result = Reject(http);
            return;
        }

        await next();
    }

    /// Có access token ⇒ family trong claim fid. Không có ⇒ family của refresh cookie.
    /// Không xác định được family ⇒ request không thể đổi trạng thái phiên nào, để handler xử lý (401/204).
    private async Task<Guid?> ResolveFamilyIdAsync(HttpContext http)
    {
        if (Guid.TryParse(http.User.FindFirst("fid")?.Value, out var fid)) return fid;
        var refreshToken = http.Request.Cookies[AuthCookieWriter.RefreshCookie];
        return string.IsNullOrEmpty(refreshToken)
            ? null
            : await _sessions.FindFamilyIdByTokenHashAsync(_refreshTokens.Hash(refreshToken), http.RequestAborted);
    }

    private static Microsoft.AspNetCore.Mvc.ObjectResult Reject(HttpContext http)
        => ProblemResponseWriter.ToResult(http, StatusCodes.Status403Forbidden, ErrorCodes.CsrfFailed,
            "Yêu cầu không hợp lệ (CSRF).");
}
```

`Auth/CsrfProtectedAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Auth;

[AttributeUsage(AttributeTargets.Method)]
public sealed class CsrfProtectedAttribute : TypeFilterAttribute
{
    public CsrfProtectedAttribute() : base(typeof(CsrfProtectionFilter)) { }
}
```

`AuthController.cs` — thêm `using CleanArchCqrs.API.Errors;`, `using CleanArchCqrs.Application.Auth;`, `using CleanArchCqrs.Application.Auth.Commands.Logout;`, `using CleanArchCqrs.Application.Auth.Commands.Refresh;`, `using CleanArchCqrs.Application.Common.Exceptions;` và các action:

```csharp
    [AllowAnonymous]
    [CsrfProtected]
    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh(CancellationToken ct)
    {
        var refreshToken = Request.Cookies[AuthCookieWriter.RefreshCookie];
        if (string.IsNullOrEmpty(refreshToken)) return SessionInvalid(AuthMessages.SessionInvalid);

        try
        {
            var session = await _mediator.Send(new RefreshCommand(refreshToken), ct);
            _cookies.Write(Response, session.RefreshToken, session.SessionFamilyId, session.SessionExpiresAtUtc);
            return Ok(new AccessTokenResponse(session.AccessToken, session.AccessTokenExpiresAtUtc, session.MustChangePassword));
        }
        catch (UnauthorizedException ex)
        {
            // Trả lỗi tại đây (không để exception handler) vì handler xoá sạch header, gồm cả Set-Cookie xoá cookie.
            return SessionInvalid(ex.Message);
        }
    }

    [AllowAnonymous]
    [CsrfProtected]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await _mediator.Send(new LogoutCommand(Request.Cookies[AuthCookieWriter.RefreshCookie]), ct);
        _cookies.Clear(Response);
        return NoContent();
    }

    private IActionResult SessionInvalid(string message)
    {
        _cookies.Clear(Response);
        return ProblemResponseWriter.ToResult(HttpContext, StatusCodes.Status401Unauthorized, ErrorCodes.Unauthenticated, message);
    }
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter RefreshAndLogoutTests`
Expected: PASS 9/9.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(auth): refresh token rotation with strict reuse detection, logout, CSRF protection"
```

---

### Task 3.6: `me`, danh sách phiên, logout-all, thu hồi một phiên

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Models/UserAccess.cs`
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/IIdentityReadService.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Models/RoleRefDto.cs`, `MeDto.cs`, `SessionDto.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Queries/GetMe/GetMeQuery.cs`, `GetMeQueryHandler.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Queries/GetMySessions/GetMySessionsQuery.cs`, `GetMySessionsQueryHandler.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Commands/LogoutAll/LogoutAllCommand.cs`, `LogoutAllCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Commands/RevokeSession/RevokeSessionCommand.cs`, `RevokeSessionCommandHandler.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Identity/EffectivePermissionsQuery.cs`, `IdentityReadService.cs`
- Create: `src/CleanArchCqrs.API/Authorization/AllowWhilePasswordChangeRequiredAttribute.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`, `src/CleanArchCqrs.API/Controllers/AuthController.cs`
- Test: `tests/CleanArchCqrs.IntegrationTests/Auth/MeAndSessionsTests.cs`

**Interfaces:**
- Consumes: `ICurrentUser.UserId/SessionFamilyId` (3.3), `ISessionRepository.GetActiveByUserForUpdateAsync/GetForUpdateAsync`.
- Produces:
  - `record UserAccess(IReadOnlySet<string> Permissions, bool MustChangePassword)` + `static UserAccess None`.
  - `IIdentityReadService`: `Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default)`; `Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionFamilyId, DateTimeOffset now, CancellationToken ct = default)`. (Giai đoạn 4 thêm method.)
  - `record RoleRefDto(Guid Id, string Code, string Name)`; `record MeDto(Guid Id, string Email, string FullName, string? AvatarUrl, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<string> Permissions, bool MustChangePassword)`; `record SessionDto(Guid Id, DateTimeOffset CreatedAtUtc, DateTimeOffset? LastRefreshedAtUtc, DateTimeOffset AbsoluteExpiresAtUtc, string? IpAddress, string? UserAgent, bool IsCurrent)`.
  - `GetMeQuery : IRequest<MeDto>`, `GetMySessionsQuery : IRequest<IReadOnlyList<SessionDto>>`, `LogoutAllCommand : IRequest`, `record RevokeSessionCommand(Guid SessionId) : IRequest` (không thuộc user ⇒ `NotFoundException` → 404).
  - `EffectivePermissionsQuery.LoadAsync(AppDbContext db, Guid userId, CancellationToken ct) : Task<UserAccess?>` (null = không có user; user khoá ⇒ tập rỗng).
  - `[AllowWhilePasswordChangeRequired]` — metadata; Giai đoạn 4 mới đọc.
  - Endpoint: `GET me`, `GET sessions`, `POST logout-all` (CSRF), `POST sessions/{id:guid}/revoke` (CSRF).

- [ ] **Step 1: Viết test (đỏ)**

`Auth/MeAndSessionsTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class MeAndSessionsTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public MeAndSessionsTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<AuthTestClient> LoginAsync(string email)
    {
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return client;
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401ProblemDetails()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient()).GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Me_ReturnsRolesAndEffectivePermissionsWithoutPasswordHash()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email, roleCodes: [SystemRoles.Admin]);
        var client = await LoginAsync(email);

        var response = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("passwordHash", raw, StringComparison.OrdinalIgnoreCase);
        using var body = JsonDocument.Parse(raw);
        Assert.Equal(email, body.RootElement.GetProperty("email").GetString());
        Assert.Equal("admin", body.RootElement.GetProperty("roles")[0].GetProperty("code").GetString());
        Assert.Contains(body.RootElement.GetProperty("permissions").EnumerateArray(), p => p.GetString() == Permissions.Users.Read);
    }

    [Fact]
    public async Task Sessions_ListsActiveSessionsAndMarksCurrent()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var first = await LoginAsync(email);
        await LoginAsync(email);

        var sessions = await (await first.GetAsync("/api/v1/auth/sessions")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(2, sessions.GetArrayLength());
        var currentFid = new JsonWebTokenHandler().ReadJsonWebToken(first.AccessToken).GetClaim("fid").Value;
        var current = Assert.Single(sessions.EnumerateArray(), s => s.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(currentFid, current.GetProperty("id").GetString());
    }

    [Fact]
    public async Task RevokeSession_OwnOtherSession_KillsItsRefresh()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var laptop = await LoginAsync(email);
        var phone = await LoginAsync(email);
        var phoneFid = new JsonWebTokenHandler().ReadJsonWebToken(phone.AccessToken).GetClaim("fid").Value;

        var response = await laptop.SendAsync(HttpMethod.Post, $"/api/v1/auth/sessions/{phoneFid}/revoke");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await phone.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await laptop.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task RevokeSession_SomeoneElsesSession_Returns404()
    {
        var mine = TestData.NewEmail();
        var theirs = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, mine);
        await TestData.CreateUserAsync(_factory, theirs);
        var me = await LoginAsync(mine);
        var other = await LoginAsync(theirs);
        var otherFid = new JsonWebTokenHandler().ReadJsonWebToken(other.AccessToken).GetClaim("fid").Value;

        var response = await me.SendAsync(HttpMethod.Post, $"/api/v1/auth/sessions/{otherFid}/revoke");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.RefreshAsync()).StatusCode);
    }

    [Fact]
    public async Task LogoutAll_KillsEverySession()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var first = await LoginAsync(email);
        var second = await LoginAsync(email);

        var response = await first.SendAsync(HttpMethod.Post, "/api/v1/auth/logout-all");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Null(first.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await second.RefreshAsync()).StatusCode);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter MeAndSessionsTests`
Expected: FAIL (404 cho endpoint mới; biên dịch lỗi nếu chưa có type).

- [ ] **Step 3: Application — model, read service, query, command**

`Common/Models/UserAccess.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Models;

/// Quyền hành động hiệu lực của một user (∪ quyền các role ∪ quyền cấp thêm; rỗng nếu tài khoản bị khoá).
public sealed record UserAccess(IReadOnlySet<string> Permissions, bool MustChangePassword)
{
    public static UserAccess None { get; } = new(new HashSet<string>(), false);
}
```

`Auth/Models/RoleRefDto.cs`:

```csharp
namespace CleanArchCqrs.Application.Auth.Models;

public sealed record RoleRefDto(Guid Id, string Code, string Name);
```

`Auth/Models/MeDto.cs`:

```csharp
namespace CleanArchCqrs.Application.Auth.Models;

/// Không bao giờ chứa PasswordHash — map thủ công từng field.
public sealed record MeDto(
    Guid Id,
    string Email,
    string FullName,
    string? AvatarUrl,
    IReadOnlyList<RoleRefDto> Roles,
    IReadOnlyList<string> Permissions,
    bool MustChangePassword);
```

`Auth/Models/SessionDto.cs`:

```csharp
namespace CleanArchCqrs.Application.Auth.Models;

public sealed record SessionDto(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastRefreshedAtUtc,
    DateTimeOffset AbsoluteExpiresAtUtc,
    string? IpAddress,
    string? UserAgent,
    bool IsCurrent);
```

`Common/Interfaces/IIdentityReadService.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Query projection (AsNoTracking) cho module IdentityAccess.
public interface IIdentityReadService
{
    Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionFamilyId, DateTimeOffset now,
        CancellationToken ct = default);
}
```

`Auth/Queries/GetMe/GetMeQuery.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMe;

public sealed record GetMeQuery : IRequest<MeDto>;
```

`Auth/Queries/GetMe/GetMeQueryHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMe;

public sealed class GetMeQueryHandler : IRequestHandler<GetMeQuery, MeDto>
{
    private readonly ICurrentUser _currentUser;
    private readonly IIdentityReadService _read;

    public GetMeQueryHandler(ICurrentUser currentUser, IIdentityReadService read)
    {
        _currentUser = currentUser;
        _read = read;
    }

    public async Task<MeDto> Handle(GetMeQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        return await _read.GetMeAsync(userId, ct) ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
    }
}
```

`Auth/Queries/GetMySessions/GetMySessionsQuery.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMySessions;

public sealed record GetMySessionsQuery : IRequest<IReadOnlyList<SessionDto>>;
```

`Auth/Queries/GetMySessions/GetMySessionsQueryHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.GetMySessions;

public sealed class GetMySessionsQueryHandler : IRequestHandler<GetMySessionsQuery, IReadOnlyList<SessionDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly IIdentityReadService _read;
    private readonly TimeProvider _time;

    public GetMySessionsQueryHandler(ICurrentUser currentUser, IIdentityReadService read, TimeProvider time)
    {
        _currentUser = currentUser;
        _read = read;
        _time = time;
    }

    public async Task<IReadOnlyList<SessionDto>> Handle(GetMySessionsQuery request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        return await _read.GetActiveSessionsAsync(userId, _currentUser.SessionFamilyId, _time.GetUtcNow(), ct);
    }
}
```

`Auth/Commands/LogoutAll/LogoutAllCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.LogoutAll;

public sealed record LogoutAllCommand : IRequest;
```

`Auth/Commands/LogoutAll/LogoutAllCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.LogoutAll;

public sealed class LogoutAllCommandHandler : IRequestHandler<LogoutAllCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly ISessionRepository _sessions;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public LogoutAllCommandHandler(ICurrentUser currentUser, ISessionRepository sessions, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _sessions = sessions;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(LogoutAllCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var now = _time.GetUtcNow();

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var families = await _sessions.GetActiveByUserForUpdateAsync(userId, ct);
        foreach (var family in families)
        {
            family.Revoke(SessionRevokeReason.LogoutAll, now);
            _cacheInvalidator.InvalidateSession(family.Id);
        }
        _audit.Record(AuditActions.LogoutAll, AuditResult.Succeeded,
            metadata: new Dictionary<string, object?> { ["sessions"] = families.Count });
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

`Auth/Commands/RevokeSession/RevokeSessionCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.RevokeSession;

public sealed record RevokeSessionCommand(Guid SessionId) : IRequest;
```

`Auth/Commands/RevokeSession/RevokeSessionCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.RevokeSession;

public sealed class RevokeSessionCommandHandler : IRequestHandler<RevokeSessionCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly ISessionRepository _sessions;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public RevokeSessionCommandHandler(ICurrentUser currentUser, ISessionRepository sessions, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _sessions = sessions;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(RevokeSessionCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);

        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var family = await _sessions.GetForUpdateAsync(request.SessionId, presentedTokenHash: null, ct);
        // 404 cả khi phiên thuộc người khác — không để lộ ID phiên có tồn tại.
        if (family is null || family.UserId != userId)
            throw new NotFoundException($"Session '{request.SessionId}' was not found.");
        if (family.Status != SessionStatus.Active) return;

        family.Revoke(SessionRevokeReason.UserRevoked, _time.GetUtcNow());
        _audit.Record(AuditActions.SessionRevoke, AuditResult.Succeeded,
            resourceType: nameof(SessionFamily), resourceId: family.Id.ToString());
        _cacheInvalidator.InvalidateSession(family.Id);
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

- [ ] **Step 4: Infrastructure — read service**

`Identity/EffectivePermissionsQuery.cs`:

```csharp
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Identity;

/// Một nơi duy nhất tính quyền hiệu lực từ DB (dùng bởi /me và PermissionService).
internal static class EffectivePermissionsQuery
{
    public static async Task<UserAccess?> LoadAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.MustChangePassword })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;
        if (!user.IsActive) return new UserAccess(new HashSet<string>(), user.MustChangePassword);

        var fromRoles =
            from userRole in db.Set<UserRole>()
            join rolePermission in db.Set<RolePermission>() on userRole.RoleId equals rolePermission.RoleId
            where userRole.UserId == userId
            select rolePermission.PermissionCode;
        var fromGrants = db.Set<UserPermission>().Where(p => p.UserId == userId).Select(p => p.PermissionCode);

        var codes = await fromRoles.Union(fromGrants).ToListAsync(ct);
        return new UserAccess(codes.ToHashSet(StringComparer.Ordinal), user.MustChangePassword);
    }
}
```

`Identity/IdentityReadService.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Identity;

public sealed class IdentityReadService : IIdentityReadService
{
    private readonly AppDbContext _db;

    public IdentityReadService(AppDbContext db) => _db = db;

    public async Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.FullName, u.AvatarUrl })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;

        var access = await EffectivePermissionsQuery.LoadAsync(_db, userId, ct);
        return new MeDto(user.Id, user.Email, user.FullName, user.AvatarUrl,
            await LoadRoleRefsAsync(userId, ct),
            access!.Permissions.OrderBy(p => p, StringComparer.Ordinal).ToList(),
            access.MustChangePassword);
    }

    public async Task<IReadOnlyList<SessionDto>> GetActiveSessionsAsync(Guid userId, Guid? currentSessionFamilyId,
        DateTimeOffset now, CancellationToken ct = default)
        => await _db.SessionFamilies.AsNoTracking()
            .Where(f => f.UserId == userId && f.Status == SessionStatus.Active && f.AbsoluteExpiresAtUtc > now)
            .OrderByDescending(f => f.CreatedAtUtc)
            .Select(f => new SessionDto(f.Id, f.CreatedAtUtc, f.LastRefreshedAtUtc, f.AbsoluteExpiresAtUtc,
                f.IpAddress, f.UserAgent, f.Id == currentSessionFamilyId))
            .ToListAsync(ct);

    private async Task<IReadOnlyList<RoleRefDto>> LoadRoleRefsAsync(Guid userId, CancellationToken ct)
        => await (
                from userRole in _db.Set<UserRole>()
                join role in _db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == userId
                orderby role.Code
                select new RoleRefDto(role.Id, role.Code, role.Name))
            .ToListAsync(ct);
}
```

DI — thêm `services.AddScoped<IIdentityReadService, IdentityReadService>();` (và `using CleanArchCqrs.Infrastructure.Identity;`).

- [ ] **Step 5: API**

`Authorization/AllowWhilePasswordChangeRequiredAttribute.cs`:

```csharp
namespace CleanArchCqrs.API.Authorization;

/// Endpoint vẫn dùng được khi tài khoản đang bị bắt đổi mật khẩu (Spec §3.9).
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class AllowWhilePasswordChangeRequiredAttribute : Attribute
{
}
```

`AuthController.cs` — thêm using `CleanArchCqrs.API.Authorization`, `CleanArchCqrs.Application.Auth.Models`, `CleanArchCqrs.Application.Auth.Queries.GetMe`, `CleanArchCqrs.Application.Auth.Queries.GetMySessions`, `CleanArchCqrs.Application.Auth.Commands.LogoutAll`, `CleanArchCqrs.Application.Auth.Commands.RevokeSession` và các action:

```csharp
    [AllowWhilePasswordChangeRequired]
    [HttpGet("me")]
    public async Task<ActionResult<MeDto>> Me(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMeQuery(), ct));

    [HttpGet("sessions")]
    public async Task<ActionResult<IReadOnlyList<SessionDto>>> Sessions(CancellationToken ct)
        => Ok(await _mediator.Send(new GetMySessionsQuery(), ct));

    [CsrfProtected]
    [HttpPost("sessions/{id:guid}/revoke")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new RevokeSessionCommand(id), ct);
        return NoContent();
    }

    [AllowWhilePasswordChangeRequired]
    [CsrfProtected]
    [HttpPost("logout-all")]
    public async Task<IActionResult> LogoutAll(CancellationToken ct)
    {
        await _mediator.Send(new LogoutAllCommand(), ct);
        _cookies.Clear(Response);
        return NoContent();
    }
```

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter MeAndSessionsTests`
Expected: PASS 6/6.

- [ ] **Step 7: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(auth): add me, session list, single-session revoke and logout-all"
```

---

### Task 3.7: Đổi mật khẩu (giữ phiên hiện tại, giết phiên khác)

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Security/PasswordPolicy.cs`, `PasswordRuleExtensions.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Models/AccessTokenResult.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Commands/ChangePassword/ChangePasswordCommand.cs`, `ChangePasswordCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Validators/ChangePasswordCommandValidator.cs`
- Modify: `src/CleanArchCqrs.API/Controllers/AuthController.cs`
- Test: Create `tests/CleanArchCqrs.UnitTests/Application/Security/PasswordPolicyTests.cs`, `tests/CleanArchCqrs.IntegrationTests/Helpers/AdminSession.cs`, `tests/CleanArchCqrs.IntegrationTests/Auth/ChangePasswordTests.cs`

**Interfaces:**
- Consumes: `User.ChangePassword` (2.3), `ISessionRepository.GetActiveByUserForUpdateAsync`, `ICacheInvalidator`, `ITokenService`.
- Produces:
  - `PasswordPolicy.MinLength = 10`, `MaxLength = 128`, `bool ContainsEmailLocalPart(string password, string email)`.
  - `IRuleBuilderOptions<T,string> NewPassword<T>(this IRuleBuilder<T,string>)` (FluentValidation).
  - `record AccessTokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword)`.
  - `record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<AccessTokenResult>` — sai mật khẩu hiện tại ⇒ 400 `errors.currentPassword`; revoke mọi family khác; **xoá** `session:{currentFid}` (Gateway tự nạp lại với `sv` mới qua BE) và `perm:{uid}`.
  - Endpoint `POST change-password` (`[CsrfProtected]`, `[AllowWhilePasswordChangeRequired]`).
  - Test helper `ApiFactory.LoginAsAdminAsync()` (extension) — đăng nhập Admin seed, tự đổi mật khẩu tạm lần đầu.

- [ ] **Step 1: Viết test (đỏ)**

`tests/CleanArchCqrs.UnitTests/Application/Security/PasswordPolicyTests.cs`:

```csharp
using CleanArchCqrs.Application.Common.Security;
using Xunit;

namespace CleanArchCqrs.UnitTests.Application.Security;

public class PasswordPolicyTests
{
    [Theory]
    [InlineData("nguyenvana-2026!", "NguyenVanA@benhvien.vn", true)]
    [InlineData("Xyz-1234567", "nguyenvana@benhvien.vn", false)]
    public void ContainsEmailLocalPart_IsCaseInsensitive(string password, string email, bool expected)
        => Assert.Equal(expected, PasswordPolicy.ContainsEmailLocalPart(password, email));
}
```

`tests/CleanArchCqrs.IntegrationTests/Helpers/AdminSession.cs`:

```csharp
using System.Net;
using CleanArchCqrs.IntegrationTests.Infrastructure;

namespace CleanArchCqrs.IntegrationTests.Helpers;

public static class AdminSession
{
    /// Admin seed của factory. Lần đầu: đăng nhập bằng mật khẩu tạm rồi đổi sang AdminPassword.
    public static async Task<AuthTestClient> LoginAsAdminAsync(this ApiFactory factory)
    {
        var client = new AuthTestClient(factory.CreateHttpsClient());
        if ((await client.LoginAsync(factory.AdminEmail, TestConstants.AdminPassword)).StatusCode == HttpStatusCode.OK)
            return client;

        (await client.LoginAsync(factory.AdminEmail, TestConstants.AdminTempPassword)).EnsureSuccessStatusCode();
        (await client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = TestConstants.AdminTempPassword, newPassword = TestConstants.AdminPassword }))
            .EnsureSuccessStatusCode();
        return client;
    }
}
```

`Auth/ChangePasswordTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class ChangePasswordTests : IAsyncLifetime
{
    private const string NewPassword = "Brand-New-Pass-99";
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public ChangePasswordTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(string Email, AuthTestClient Client)> LoggedInAsync()
    {
        var email = TestData.NewEmail("staff");
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (email, client);
    }

    private static Task<HttpResponseMessage> ChangeAsync(AuthTestClient client, string current, string next, bool csrf = true)
        => client.SendAsync(HttpMethod.Post, "/api/v1/auth/change-password",
            new { currentPassword = current, newPassword = next }, csrf: csrf);

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors");

    [Fact]
    public async Task WrongCurrentPassword_Returns400OnCurrentPassword()
    {
        var (_, client) = await LoggedInAsync();

        var response = await ChangeAsync(client, "Not-My-Password-1", NewPassword);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("currentPassword", out _));
    }

    [Theory]
    [InlineData("short")]
    [InlineData(TestData.DefaultPassword)]
    public async Task InvalidNewPassword_Returns400OnNewPassword(string next)
    {
        var (_, client) = await LoggedInAsync();

        var response = await ChangeAsync(client, TestData.DefaultPassword, next);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("newPassword", out _));
    }

    [Fact]
    public async Task NewPasswordContainingEmailName_Returns400()
    {
        var (email, client) = await LoggedInAsync();
        var local = email[..email.IndexOf('@')];

        var response = await ChangeAsync(client, TestData.DefaultPassword, $"{local}-X1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MissingCsrf_Returns403()
    {
        var (_, client) = await LoggedInAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await ChangeAsync(client, TestData.DefaultPassword, NewPassword, csrf: false)).StatusCode);
    }

    [Fact]
    public async Task Success_BumpsSecurityVersion_KeepsCurrentSession_KillsOthers()
    {
        var (email, current) = await LoggedInAsync();
        var other = new AuthTestClient(_factory.CreateHttpsClient());
        (await other.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        var oldSv = int.Parse(new JsonWebTokenHandler().ReadJsonWebToken(current.AccessToken).GetClaim("sv").Value);

        var response = await ChangeAsync(current, TestData.DefaultPassword, NewPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(current.AccessToken);
        Assert.Equal(oldSv + 1, int.Parse(jwt.GetClaim("sv").Value));
        Assert.False((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal(HttpStatusCode.Unauthorized, (await other.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await current.RefreshAsync()).StatusCode);
        var redis = _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        Assert.False(await redis.KeyExistsAsync(CacheKeys.Session(Guid.Parse(jwt.GetClaim("fid").Value))));
    }

    [Fact]
    public async Task SeededAdmin_MustChangeThenCanLogInWithNewPassword()
    {
        var admin = await _factory.LoginAsAdminAsync();

        var me = await (await admin.GetAsync("/api/v1/auth/me")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(me.GetProperty("mustChangePassword").GetBoolean());
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter PasswordPolicyTests; dotnet test tests/CleanArchCqrs.IntegrationTests --filter ChangePasswordTests`
Expected: FAIL.

- [ ] **Step 3: Application**

`Common/Security/PasswordPolicy.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Security;

/// Theo hướng NIST: đủ dài, không ép kiểu ký tự, không đổi định kỳ.
public static class PasswordPolicy
{
    public const int MinLength = 10;
    public const int MaxLength = 128;

    public static bool ContainsEmailLocalPart(string password, string email)
    {
        var at = email.IndexOf('@');
        var local = at > 0 ? email[..at] : email;
        return local.Length > 0 && password.Contains(local, StringComparison.OrdinalIgnoreCase);
    }
}
```

`Common/Security/PasswordRuleExtensions.cs`:

```csharp
using FluentValidation;

namespace CleanArchCqrs.Application.Common.Security;

public static class PasswordRuleExtensions
{
    public static IRuleBuilderOptions<T, string> NewPassword<T>(this IRuleBuilder<T, string> rule)
        => rule
            .NotEmpty().WithMessage("Mật khẩu không được để trống.")
            .Length(PasswordPolicy.MinLength, PasswordPolicy.MaxLength)
            .WithMessage($"Mật khẩu phải dài từ {PasswordPolicy.MinLength} đến {PasswordPolicy.MaxLength} ký tự.");
}
```

`Auth/Models/AccessTokenResult.cs`:

```csharp
namespace CleanArchCqrs.Application.Auth.Models;

public sealed record AccessTokenResult(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword);
```

`Auth/Commands/ChangePassword/ChangePasswordCommand.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.ChangePassword;

public sealed record ChangePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<AccessTokenResult>;
```

`Auth/Validators/ChangePasswordCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Commands.ChangePassword;
using CleanArchCqrs.Application.Common.Security;
using FluentValidation;

namespace CleanArchCqrs.Application.Auth.Validators;

public sealed class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.CurrentPassword).NotEmpty();
        RuleFor(x => x.NewPassword).NewPassword();
    }
}
```

`Auth/Commands/ChangePassword/ChangePasswordCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Security;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler : IRequestHandler<ChangePasswordCommand, AccessTokenResult>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly ISessionRepository _sessions;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public ChangePasswordCommandHandler(ICurrentUser currentUser, IUserRepository users, ISessionRepository sessions,
        IPasswordHasher passwordHasher, ITokenService tokenService, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _sessions = sessions;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<AccessTokenResult> Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        var userId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var currentFamilyId = _currentUser.SessionFamilyId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var user = await _users.GetUserByIdAsync(userId, ct);

        // Người dùng đang đăng nhập hợp lệ — sai mật khẩu hiện tại là lỗi dữ liệu (400), không phải 401.
        if (!_passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new ValidationException(nameof(request.CurrentPassword), "Mật khẩu hiện tại không đúng.");
        if (_passwordHasher.Verify(request.NewPassword, user.PasswordHash))
            throw new ValidationException(nameof(request.NewPassword), "Mật khẩu mới phải khác mật khẩu hiện tại.");
        if (PasswordPolicy.ContainsEmailLocalPart(request.NewPassword, user.Email))
            throw new ValidationException(nameof(request.NewPassword), "Mật khẩu không được chứa phần tên trong email.");

        var now = _time.GetUtcNow();
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        var families = await _sessions.GetActiveByUserForUpdateAsync(user.Id, ct);
        if (families.All(f => f.Id != currentFamilyId))
            throw new UnauthorizedException(AuthMessages.SessionInvalid);

        foreach (var family in families.Where(f => f.Id != currentFamilyId))
            family.Revoke(SessionRevokeReason.PasswordChanged, now);
        foreach (var family in families)
            _cacheInvalidator.InvalidateSession(family.Id);   // kể cả phiên hiện tại: key cũ mang sv cũ
        user.ChangePassword(_passwordHasher.Hash(request.NewPassword));   // SecurityVersion++, MustChangePassword=false
        _cacheInvalidator.InvalidatePermissions(user.Id);                 // perm:{uid} lưu cờ mustChangePassword
        _audit.Record(AuditActions.PasswordChange, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);

        var access = _tokenService.CreateAccessToken(user.Id, currentFamilyId, user.SecurityVersion);
        return new AccessTokenResult(access.Token, access.ExpiresAtUtc, user.MustChangePassword);
    }
}
```

- [ ] **Step 4: API**

`AuthController.cs` — thêm `using CleanArchCqrs.Application.Auth.Commands.ChangePassword;` và:

```csharp
    [AllowWhilePasswordChangeRequired]
    [CsrfProtected]
    [HttpPost("change-password")]
    public async Task<ActionResult<AccessTokenResponse>> ChangePassword(ChangePasswordCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(new AccessTokenResponse(result.AccessToken, result.ExpiresAtUtc, result.MustChangePassword));
    }
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter PasswordPolicyTests && dotnet test tests/CleanArchCqrs.IntegrationTests --filter ChangePasswordTests`
Expected: PASS (unit 2/2, integration 7/7).

- [ ] **Step 6: Commit**

```bash
git add -A src tests
git commit -m "feat(auth): change password bumps security version, keeps current session, revokes others"
```

---

### Task 3.8: Endpoint nội bộ cho Gateway — kiểm tra phiên khi Redis không có key

**Files:**
- Create: `src/CleanArchCqrs.Application/Auth/Models/SessionValidationResult.cs`
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/ISessionValidationService.cs`
- Create: `src/CleanArchCqrs.Application/Auth/Queries/ValidateSession/ValidateSessionQuery.cs`, `ValidateSessionQueryHandler.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Identity/SessionValidationService.cs`
- Create: `src/CleanArchCqrs.API/Auth/InternalApiKeyFilter.cs`, `InternalApiKeyAttribute.cs`
- Create: `src/CleanArchCqrs.API/Contracts/Internal/ValidateSessionRequest.cs`, `ValidateSessionResponse.cs`
- Create: `src/CleanArchCqrs.API/Controllers/InternalSessionsController.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Test: `tests/CleanArchCqrs.IntegrationTests/Auth/InternalSessionValidationTests.cs`

**Interfaces:**
- Consumes: `GuardedCacheWrite`, `SessionCachePayload`, `CacheKeys` (3.2), `AuthOptions.InternalApiKey` (3.1).
- Produces:
  - `record SessionValidationResult(bool IsValid, Guid? UserId, DateTimeOffset? AbsoluteExpiresAtUtc)` + `static Invalid`.
  - `ISessionValidationService.ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default)` — kiểm DB (family Active, chưa hết hạn, user active, `SecurityVersion` khớp); hợp lệ thì nạp lại `session:{fid}` bằng **ghi có điều kiện theo thế hệ**.
  - `record ValidateSessionQuery(Guid SessionFamilyId, int SecurityVersion) : IRequest<SessionValidationResult>`.
  - HTTP: `POST /internal/sessions/validate` body `{"familyId":"<guid>","sv":<int>}` + header `X-Internal-Key` ⇒ `200 {"valid":true,"userId":"<guid>","absExp":<unix>}` hoặc `200 {"valid":false}`; sai/thiếu key ⇒ 401.

- [ ] **Step 1: Viết test (đỏ)**

`Auth/InternalSessionValidationTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Auth;

[Collection(IntegrationCollection.Name)]
public class InternalSessionValidationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public InternalSessionValidationTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private IDatabase Redis => _factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();

    private async Task<(AuthTestClient Client, Guid Fid, int Sv)> LoggedInAsync()
    {
        var email = TestData.NewEmail();
        await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(client.AccessToken);
        return (client, Guid.Parse(jwt.GetClaim("fid").Value), int.Parse(jwt.GetClaim("sv").Value));
    }

    private async Task<HttpResponseMessage> ValidateAsync(Guid fid, int sv, string? key = TestConstants.InternalApiKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/sessions/validate")
        {
            Content = JsonContent.Create(new { familyId = fid, sv })
        };
        if (key is not null) request.Headers.Add("X-Internal-Key", key);
        return await _factory.CreateHttpsClient().SendAsync(request);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-key")]
    public async Task MissingOrWrongKey_Returns401(string? key)
        => Assert.Equal(HttpStatusCode.Unauthorized, (await ValidateAsync(Guid.NewGuid(), 1, key)).StatusCode);

    [Fact]
    public async Task ValidSession_ReturnsValidAndReloadsCache()
    {
        var (_, fid, sv) = await LoggedInAsync();
        await Redis.KeyDeleteAsync(CacheKeys.Session(fid));

        var body = await (await ValidateAsync(fid, sv)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("valid").GetBoolean());
        Assert.True(body.GetProperty("absExp").GetInt64() > DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        Assert.True(await Redis.KeyExistsAsync(CacheKeys.Session(fid)));
    }

    [Fact]
    public async Task StaleSecurityVersion_IsInvalid()
    {
        var (_, fid, sv) = await LoggedInAsync();

        var body = await (await ValidateAsync(fid, sv - 1)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task LoggedOutSession_IsInvalidAndNotRecached()
    {
        var (client, fid, sv) = await LoggedInAsync();
        await client.SendAsync(HttpMethod.Post, "/api/v1/auth/logout", bearer: false);

        var body = await (await ValidateAsync(fid, sv)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("valid").GetBoolean());
        Assert.False(await Redis.KeyExistsAsync(CacheKeys.Session(fid)));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter InternalSessionValidationTests`
Expected: FAIL — 404.

- [ ] **Step 3: Application**

`Auth/Models/SessionValidationResult.cs`:

```csharp
namespace CleanArchCqrs.Application.Auth.Models;

public sealed record SessionValidationResult(bool IsValid, Guid? UserId, DateTimeOffset? AbsoluteExpiresAtUtc)
{
    public static SessionValidationResult Invalid { get; } = new(false, null, null);
}
```

`Common/Interfaces/ISessionValidationService.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Nguồn sự thật (DB) cho Gateway khi Redis không có session:{fid}. Hợp lệ thì nạp lại cache.
public interface ISessionValidationService
{
    Task<SessionValidationResult> ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default);
}
```

`Auth/Queries/ValidateSession/ValidateSessionQuery.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.ValidateSession;

public sealed record ValidateSessionQuery(Guid SessionFamilyId, int SecurityVersion) : IRequest<SessionValidationResult>;
```

`Auth/Queries/ValidateSession/ValidateSessionQueryHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Interfaces;
using MediatR;

namespace CleanArchCqrs.Application.Auth.Queries.ValidateSession;

public sealed class ValidateSessionQueryHandler : IRequestHandler<ValidateSessionQuery, SessionValidationResult>
{
    private readonly ISessionValidationService _validation;

    public ValidateSessionQueryHandler(ISessionValidationService validation) => _validation = validation;

    public Task<SessionValidationResult> Handle(ValidateSessionQuery request, CancellationToken ct)
        => _validation.ValidateAsync(request.SessionFamilyId, request.SecurityVersion, ct);
}
```

- [ ] **Step 4: Infrastructure**

`Identity/SessionValidationService.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity.Sessions;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Identity;

public sealed class SessionValidationService : ISessionValidationService
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeProvider _time;
    private readonly ILogger<SessionValidationService> _logger;

    public SessionValidationService(AppDbContext db, IConnectionMultiplexer redis, TimeProvider time,
        ILogger<SessionValidationService> logger)
    {
        _db = db;
        _redis = redis;
        _time = time;
        _logger = logger;
    }

    public async Task<SessionValidationResult> ValidateAsync(Guid sessionFamilyId, int securityVersion, CancellationToken ct = default)
    {
        var key = CacheKeys.Session(sessionFamilyId);
        var generation = await TryReadGenerationAsync(key);   // TRƯỚC khi đọc DB

        var row = await _db.SessionFamilies.AsNoTracking()
            .Where(f => f.Id == sessionFamilyId)
            .Join(_db.Users, f => f.UserId, u => u.Id,
                (f, u) => new { f.Status, f.AbsoluteExpiresAtUtc, UserId = u.Id, u.IsActive, u.SecurityVersion })
            .SingleOrDefaultAsync(ct);

        var now = _time.GetUtcNow();
        if (row is null || row.Status != SessionStatus.Active || row.AbsoluteExpiresAtUtc <= now
            || !row.IsActive || row.SecurityVersion != securityVersion)
            return SessionValidationResult.Invalid;

        if (generation is { } readGeneration)
            await TryRecacheAsync(key, SessionCachePayload.Serialize(row.UserId, row.SecurityVersion, row.AbsoluteExpiresAtUtc),
                readGeneration, row.AbsoluteExpiresAtUtc - now);

        return new SessionValidationResult(true, row.UserId, row.AbsoluteExpiresAtUtc);
    }

    /// null = Redis lỗi ⇒ bỏ qua bước ghi cache.
    private async Task<RedisValue?> TryReadGenerationAsync(string key)
    {
        try
        {
            return await GuardedCacheWrite.ReadGenerationAsync(_redis.GetDatabase(), key);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable while validating session");
            return null;
        }
    }

    private async Task TryRecacheAsync(string key, string payload, RedisValue generation, TimeSpan ttl)
    {
        try
        {
            await GuardedCacheWrite.SetIfUnchangedAsync(_redis.GetDatabase(), key, payload, generation, ttl);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not re-cache session");
        }
    }
}
```

DI — thêm `services.AddScoped<ISessionValidationService, SessionValidationService>();`.

- [ ] **Step 5: API**

`Auth/InternalApiKeyFilter.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Auth;

/// /internal/* chỉ Gateway gọi. Gateway không có route ra ngoài cho /internal, cộng thêm khoá chung này.
public sealed class InternalApiKeyFilter : IAsyncAuthorizationFilter
{
    public const string HeaderName = "X-Internal-Key";
    private readonly byte[] _expected;

    public InternalApiKeyFilter(IOptions<AuthOptions> options)
        => _expected = Encoding.UTF8.GetBytes(options.Value.InternalApiKey);

    public Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var provided = Encoding.UTF8.GetBytes(context.HttpContext.Request.Headers[HeaderName].ToString());
        if (_expected.Length == 0 || !CryptographicOperations.FixedTimeEquals(provided, _expected))
            context.Result = ProblemResponseWriter.ToResult(context.HttpContext, StatusCodes.Status401Unauthorized,
                ErrorCodes.Unauthenticated, "Thiếu hoặc sai khoá nội bộ.");
        return Task.CompletedTask;
    }
}
```

`Auth/InternalApiKeyAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Auth;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class InternalApiKeyAttribute : TypeFilterAttribute
{
    public InternalApiKeyAttribute() : base(typeof(InternalApiKeyFilter)) { }
}
```

`Contracts/Internal/ValidateSessionRequest.cs`:

```csharp
namespace CleanArchCqrs.API.Contracts.Internal;

public sealed record ValidateSessionRequest(Guid FamilyId, int Sv);
```

`Contracts/Internal/ValidateSessionResponse.cs`:

```csharp
namespace CleanArchCqrs.API.Contracts.Internal;

/// absExp: Unix seconds — cùng định dạng với giá trị session:{fid} Gateway đọc từ Redis.
public sealed record ValidateSessionResponse(bool Valid, Guid? UserId, long? AbsExp);
```

`Controllers/InternalSessionsController.cs`:

```csharp
using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Contracts.Internal;
using CleanArchCqrs.Application.Auth.Queries.ValidateSession;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("internal/sessions")]
[AllowAnonymous]
[InternalApiKey]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class InternalSessionsController : ControllerBase
{
    private readonly ISender _mediator;

    public InternalSessionsController(ISender mediator) => _mediator = mediator;

    [HttpPost("validate")]
    public async Task<ActionResult<ValidateSessionResponse>> Validate(ValidateSessionRequest request, CancellationToken ct)
    {
        var result = await _mediator.Send(new ValidateSessionQuery(request.FamilyId, request.Sv), ct);
        return Ok(new ValidateSessionResponse(result.IsValid, result.UserId, result.AbsoluteExpiresAtUtc?.ToUnixTimeSeconds()));
    }
}
```

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter InternalSessionValidationTests`
Expected: PASS 5/5.

Run: `dotnet build && dotnet test`
Expected: 0 warning; toàn bộ xanh.

- [ ] **Step 7: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(auth): add internal session validation endpoint for the gateway with guarded cache reload"
```

---

### Task 3.9: Hạ tầng audit cho truy vấn nhạy cảm (`IAuditedRequest`)

Chưa có query nào dùng ở spec này — chuẩn bị cho module lâm sàng (Đặc tả kỹ thuật §3.3: "ghi audit bền vững trước khi cung cấp dữ liệu nhạy cảm; ghi thất bại thì trả lỗi và không phát dữ liệu").

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/IAuditedRequest.cs`
- Create: `src/CleanArchCqrs.Application/Common/Behaviors/AuditBehavior.cs`
- Modify: `src/CleanArchCqrs.Application/DependencyInjection/ApplicationServiceExtensions.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Application/Behaviors/AuditBehaviorTests.cs`

**Interfaces:**
- Consumes: `IAuditRecorder` (3.3), `IUnitOfWork`.
- Produces:
  - `IAuditedRequest { string AuditAction { get; } string AuditResourceType { get; } string? AuditResourceId { get; } }` — request MediatR implement interface này thì mọi lần trả dữ liệu đều được audit.
  - `AuditBehavior<TRequest,TResponse>` — chạy handler, ghi `AuditRecord(Succeeded)` và `SaveChangesAsync` **trước khi** trả response; lưu audit lỗi ⇒ exception lan ra ⇒ client không nhận dữ liệu. Request không implement ⇒ đi thẳng.

- [ ] **Step 1: Viết test (đỏ)**

`tests/CleanArchCqrs.UnitTests/Application/Behaviors/AuditBehaviorTests.cs`:

```csharp
using CleanArchCqrs.Application.Common.Behaviors;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using Xunit;

namespace CleanArchCqrs.UnitTests.Application.Behaviors;

sealed record SensitiveQuery(string Id) : IAuditedRequest
{
    public string AuditAction => "records.read";
    public string AuditResourceType => "MedicalRecord";
    public string? AuditResourceId => Id;
}

sealed record PlainQuery;

sealed class RecordingAuditRecorder : IAuditRecorder
{
    public List<(string Action, AuditResult Result, string? ResourceId)> Records { get; } = new();

    public void Record(string action, AuditResult result, string? reason = null, string? resourceType = null,
        string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)
        => Records.Add((action, result, resourceId));
}

sealed class StubUnitOfWork : IUnitOfWork
{
    public bool Fail { get; init; }
    public int Saves { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        if (Fail) throw new InvalidOperationException("audit store down");
        Saves++;
        return Task.FromResult(1);
    }

    public Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken ct = default) => throw new NotSupportedException();
}

public class AuditBehaviorTests
{
    [Fact]
    public async Task AuditedRequest_IsRecordedAndSavedBeforeDataIsReturned()
    {
        var recorder = new RecordingAuditRecorder();
        var unitOfWork = new StubUnitOfWork();
        var behavior = new AuditBehavior<SensitiveQuery, string>(recorder, unitOfWork);

        var result = await behavior.Handle(new SensitiveQuery("r-1"), () => Task.FromResult("secret"), CancellationToken.None);

        Assert.Equal("secret", result);
        Assert.Equal(("records.read", AuditResult.Succeeded, "r-1"), Assert.Single(recorder.Records));
        Assert.Equal(1, unitOfWork.Saves);
    }

    [Fact]
    public async Task AuditStoreFailure_WithholdsTheData()
        => await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AuditBehavior<SensitiveQuery, string>(new RecordingAuditRecorder(), new StubUnitOfWork { Fail = true })
                .Handle(new SensitiveQuery("r-1"), () => Task.FromResult("secret"), CancellationToken.None));

    [Fact]
    public async Task PlainRequest_IsNotAudited()
    {
        var recorder = new RecordingAuditRecorder();

        await new AuditBehavior<PlainQuery, int>(recorder, new StubUnitOfWork())
            .Handle(new PlainQuery(), () => Task.FromResult(1), CancellationToken.None);

        Assert.Empty(recorder.Records);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter AuditBehaviorTests`
Expected: FAIL biên dịch.

- [ ] **Step 3: Code**

`Common/Interfaces/IAuditedRequest.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Interfaces;

/// Đánh dấu query/command trả dữ liệu nhạy cảm (bệnh án, kết quả CLS…). Khai báo tường minh —
/// không suy từ kiểu dữ liệu trả về (Đặc tả kỹ thuật §3.3).
public interface IAuditedRequest
{
    string AuditAction { get; }
    string AuditResourceType { get; }
    string? AuditResourceId { get; }
}
```

`Common/Behaviors/AuditBehavior.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using MediatR;

namespace CleanArchCqrs.Application.Common.Behaviors;

/// Ghi audit bền vững TRƯỚC khi trả dữ liệu; lưu thất bại ⇒ exception ⇒ dữ liệu không rời server.
public sealed class AuditBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public AuditBehavior(IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken)
    {
        if (request is not IAuditedRequest audited) return await next();

        var response = await next();
        _audit.Record(audited.AuditAction, AuditResult.Succeeded,
            resourceType: audited.AuditResourceType, resourceId: audited.AuditResourceId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return response;
    }
}
```

`ApplicationServiceExtensions.cs` — thêm sau dòng đăng ký `ValidationBehavior`:

```csharp
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditBehavior<,>));
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter AuditBehaviorTests && dotnet test`
Expected: PASS 3/3; toàn bộ vẫn xanh.

- [ ] **Step 5: Commit**

```bash
git add -A src tests/CleanArchCqrs.UnitTests
git commit -m "feat(audit): add IAuditedRequest pipeline that persists audit before returning sensitive data"
```

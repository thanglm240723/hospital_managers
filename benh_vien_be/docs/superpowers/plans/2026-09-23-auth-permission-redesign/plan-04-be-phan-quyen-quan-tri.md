# Giai đoạn 4 — BE: phân quyền & API quản trị

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** BE quyết định quyền: `[HasPermission]` đọc `perm:{userId}` (Redis → DB, ghi có điều kiện theo thế hệ), mọi endpoint mặc định phải đăng nhập, chặn tài khoản `MustChangePassword`, 403 có audit; API quản trị user / role / danh mục quyền, thay đổi có hiệu lực ngay request kế tiếp.

**Spec:** [`spec.md`](spec.md) §3.9, §4.1–4.3 · **Ràng buộc chung:** [`plan.md`](plan.md#global-constraints) · **Cần xong:** Giai đoạn 3

⚠ **Không** tạo thư mục/namespace `Application/Permissions/`: namespace `CleanArchCqrs.Application.Permissions` sẽ che static class `Domain.Identity.Permissions` trong mọi file `CleanArchCqrs.Application.*` (lỗi biên dịch khó hiểu). Dùng `Application/PermissionCatalog/`.

---

### Task 4.1: `IPermissionService` — quyền hiệu lực, cache Redis không TTL

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Interfaces/IPermissionService.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Identity/PermissionCachePayload.cs`, `PermissionService.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Modify: `tests/CleanArchCqrs.IntegrationTests/Helpers/TestData.cs` (thêm `GrantAsync`)
- Test: `tests/CleanArchCqrs.IntegrationTests/Authorization/PermissionServiceTests.cs`

**Interfaces:**
- Consumes: `EffectivePermissionsQuery.LoadAsync` (3.6), `UserAccess` (3.6), `GuardedCacheWrite`, `CacheKeys`, `RedisFailure` (3.2).
- Produces:
  - `IPermissionService.GetAsync(Guid userId, CancellationToken ct = default) : Task<UserAccess>` — đọc `perm:{uid}`; miss ⇒ DB ⇒ ghi có điều kiện theo thế hệ; Redis lỗi ⇒ DB; user không tồn tại ⇒ `UserAccess.None`. Nhớ kết quả trong phạm vi request (scoped).
  - `TestData.GrantAsync(ApiFactory factory, Guid userId, params string[] permissionCodes)`.

- [ ] **Step 1: Test helper + test (đỏ)**

`Helpers/TestData.cs` — thêm method (và `using CleanArchCqrs.Infrastructure.Caching;`, `using StackExchange.Redis;`):

```csharp
    /// Cấp quyền lẻ thẳng trong DB và xoá cache quyền — dùng để dựng tình huống test, không phải để test API cấp quyền.
    public static async Task GrantAsync(ApiFactory factory, Guid userId, params string[] permissionCodes)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.Include(u => u.PermissionGrants).SingleAsync(u => u.Id == userId);
        foreach (var code in permissionCodes)
            user.GrantPermission(code, "test setup", null, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync();
        await scope.ServiceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase()
            .KeyDeleteAsync(CacheKeys.Permissions(userId));
    }
```

`Authorization/PermissionServiceTests.cs`:

```csharp
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Authorization;

[Collection(IntegrationCollection.Name)]
public class PermissionServiceTests
{
    private readonly ContainersFixture _containers;

    public PermissionServiceTests(ContainersFixture containers) => _containers = containers;

    private static async Task<UserAccess> GetAsync(ApiFactory factory, Guid userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPermissionService>().GetAsync(userId);
    }

    [Fact]
    public async Task FirstCall_LoadsFromDbAndCachesWithoutTtl()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);

        var access = await GetAsync(factory, userId);

        Assert.Contains(Permissions.Users.Read, access.Permissions);
        var redis = factory.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
        using var cached = JsonDocument.Parse((string)(await redis.StringGetAsync(CacheKeys.Permissions(userId)))!);
        Assert.Contains(cached.RootElement.GetProperty("permissions").EnumerateArray(), p => p.GetString() == Permissions.Users.Read);
        Assert.Null(await redis.KeyTimeToLiveAsync(CacheKeys.Permissions(userId)));
    }

    [Fact]
    public async Task CachedValue_IsUsedUntilInvalidated()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);
        await GetAsync(factory, userId);   // làm ấm cache

        await TestData.QueryAsync(factory, async db =>
        {
            await db.Set<UserRole>().Where(r => r.UserId == userId).ExecuteDeleteAsync();   // đổi DB "lén", không invalidate
            return 0;
        });
        Assert.Contains(Permissions.Users.Read, (await GetAsync(factory, userId)).Permissions);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var invalidator = scope.ServiceProvider.GetRequiredService<ICacheInvalidator>();
            invalidator.InvalidatePermissions(userId);
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().SaveChangesAsync();
            await invalidator.FlushAsync();
        }
        Assert.DoesNotContain(Permissions.Users.Read, (await GetAsync(factory, userId)).Permissions);
    }

    [Fact]
    public async Task RedisDown_FallsBackToDatabase()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail(), roleCodes: [SystemRoles.Admin]);

        Assert.Contains(Permissions.Users.Read, (await GetAsync(factory, userId)).Permissions);
    }

    [Fact]
    public async Task InactiveUser_HasNoPermissions_UnknownUser_HasNone()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var inactive = await TestData.CreateUserAsync(factory, TestData.NewEmail(), isActive: false, roleCodes: [SystemRoles.Admin]);

        Assert.Empty((await GetAsync(factory, inactive)).Permissions);
        Assert.Empty((await GetAsync(factory, Guid.NewGuid())).Permissions);
    }

    [Fact]
    public async Task GrantedPermission_IsPartOfEffectiveSet()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);
        var userId = await TestData.CreateUserAsync(factory, TestData.NewEmail());
        await TestData.GrantAsync(factory, userId, Permissions.Catalog.Read);

        Assert.Equal(new[] { Permissions.Catalog.Read }, (await GetAsync(factory, userId)).Permissions);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter PermissionServiceTests`
Expected: FAIL biên dịch (`IPermissionService` chưa có).

- [ ] **Step 3: Code**

`Application/Common/Interfaces/IPermissionService.cs`:

```csharp
using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Application.Common.Interfaces;

/// Quyền HÀNH ĐỘNG của user. Quyền theo tài nguyên (phân công, grant khẩn cấp) KHÔNG nằm ở đây — kiểm trong DB theo use case.
public interface IPermissionService
{
    Task<UserAccess> GetAsync(Guid userId, CancellationToken ct = default);
}
```

`Infrastructure/Identity/PermissionCachePayload.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
using CleanArchCqrs.Application.Common.Models;

namespace CleanArchCqrs.Infrastructure.Identity;

/// Giá trị perm:{userId}: {"permissions":[...],"mustChangePassword":false}
internal sealed record PermissionCachePayload(
    [property: JsonPropertyName("permissions")] string[] Permissions,
    [property: JsonPropertyName("mustChangePassword")] bool MustChangePassword)
{
    public static string Serialize(UserAccess access)
        => JsonSerializer.Serialize(new PermissionCachePayload(access.Permissions.Order(StringComparer.Ordinal).ToArray(), access.MustChangePassword));

    public static UserAccess Deserialize(string json)
    {
        var payload = JsonSerializer.Deserialize<PermissionCachePayload>(json)!;
        return new UserAccess(payload.Permissions.ToHashSet(StringComparer.Ordinal), payload.MustChangePassword);
    }
}
```

`Infrastructure/Identity/PermissionService.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Infrastructure.Caching;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.Identity;

/// perm:{userId} không TTL; đúng đắn nhờ (1) xoá chủ động qua CacheInvalidations và (2) ghi có điều kiện theo thế hệ.
public sealed class PermissionService : IPermissionService
{
    private readonly AppDbContext _db;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<PermissionService> _logger;
    private readonly Dictionary<Guid, UserAccess> _perRequest = new();

    public PermissionService(AppDbContext db, IConnectionMultiplexer redis, ILogger<PermissionService> logger)
    {
        _db = db;
        _redis = redis;
        _logger = logger;
    }

    public async Task<UserAccess> GetAsync(Guid userId, CancellationToken ct = default)
    {
        if (_perRequest.TryGetValue(userId, out var known)) return known;

        var key = CacheKeys.Permissions(userId);
        var (cached, generation) = await TryReadAsync(key);
        var access = cached ?? await EffectivePermissionsQuery.LoadAsync(_db, userId, ct) ?? UserAccess.None;

        // Không cache user không tồn tại (None); user bị khoá vẫn cache tập rỗng.
        if (cached is null && generation is { } readGeneration && !ReferenceEquals(access, UserAccess.None))
            await TryWriteAsync(key, access, readGeneration);

        _perRequest[userId] = access;
        return access;
    }

    /// Generation = null nghĩa là Redis lỗi ⇒ không ghi cache.
    private async Task<(UserAccess? Access, RedisValue? Generation)> TryReadAsync(string key)
    {
        try
        {
            var db = _redis.GetDatabase();
            var generation = await GuardedCacheWrite.ReadGenerationAsync(db, key);   // TRƯỚC khi đọc DB
            var value = await db.StringGetAsync(key);
            return (value.HasValue ? PermissionCachePayload.Deserialize(value.ToString()) : null, generation);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Redis unavailable; reading permissions from the database");
            return (null, null);
        }
    }

    private async Task TryWriteAsync(string key, UserAccess access, RedisValue generation)
    {
        try
        {
            await GuardedCacheWrite.SetIfUnchangedAsync(_redis.GetDatabase(), key, PermissionCachePayload.Serialize(access), generation, expiry: null);
        }
        catch (Exception ex) when (RedisFailure.Is(ex))
        {
            _logger.LogWarning(ex, "Could not cache permissions");
        }
    }
}
```

DI — thêm `services.AddScoped<IPermissionService, PermissionService>();`.

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter PermissionServiceTests`
Expected: PASS 5/5.

- [ ] **Step 5: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(authz): add permission service with Redis cache and generation-guarded writes"
```

---

### Task 4.2: Pipeline phân quyền — `[HasPermission]`, bắt đổi mật khẩu, 401/403 + audit, `GET /permissions`

**Files:**
- Create in `src/CleanArchCqrs.API/Authorization/`: `PermissionRequirement.cs`, `HasPermissionAttribute.cs`, `PermissionPolicyProvider.cs`, `PermissionAuthorizationHandler.cs`, `PasswordChangeRequirement.cs`, `PasswordChangeRequirementHandler.cs`, `ProblemAuthorizationResultHandler.cs`
- Create: `src/CleanArchCqrs.API/DependencyInjection/ApiAuthorizationExtensions.cs`
- Create: `src/CleanArchCqrs.Application/PermissionCatalog/Models/PermissionDto.cs`
- Create: `src/CleanArchCqrs.Application/PermissionCatalog/Queries/GetPermissions/GetPermissionsQuery.cs`, `GetPermissionsQueryHandler.cs`
- Create: `src/CleanArchCqrs.API/Controllers/PermissionsController.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs`
- Test: `tests/CleanArchCqrs.IntegrationTests/Authorization/AuthorizationPipelineTests.cs`

**Interfaces:**
- Consumes: `IPermissionService` (4.1), `AllowWhilePasswordChangeRequiredAttribute` (3.6), `IAuditRecorder`, `IUnitOfWork`, `ProblemResponseWriter`.
- Produces:
  - `[HasPermission(string permission)]` → policy `perm:<code>` = đăng nhập + `PasswordChangeRequirement` + `PermissionRequirement`.
  - `DefaultPolicy` = `FallbackPolicy` = đăng nhập + `PasswordChangeRequirement` (endpoint không khai báo gì cũng phải đăng nhập; muốn ẩn danh phải `[AllowAnonymous]`).
  - 401 → `unauthenticated` (qua JwtBearer `OnChallenge`); 403 thiếu quyền → `forbidden` + `AuditRecord(authz.denied, Denied, Metadata{permissions, path})`; 403 `password_change_required` (không audit).
  - `IServiceCollection.AddApiAuthorization()`.
  - `record PermissionDto(string Code, string Group, string Description)`; `GetPermissionsQuery : IRequest<IReadOnlyList<PermissionDto>>`; `GET /api/v1/permissions` (`permissions.read`).

- [ ] **Step 1: Viết test (đỏ)**

`Authorization/AuthorizationPipelineTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Authorization;

[Collection(IntegrationCollection.Name)]
public class AuthorizationPipelineTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;

    public AuthorizationPipelineTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync() => _factory = await ApiFactory.CreateAsync(_containers);

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid UserId, AuthTestClient Client)> UserAsync(bool mustChangePassword = false, params string[] roles)
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email, mustChangePassword: mustChangePassword, roleCodes: roles);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (userId, client);
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await new AuthTestClient(_factory.CreateHttpsClient()).GetAsync("/api/v1/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("unauthenticated", await CodeAsync(response));
    }

    [Fact]
    public async Task MissingPermission_Returns403AndIsAudited()
    {
        var (userId, client) = await UserAsync();

        var response = await client.GetAsync("/api/v1/permissions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("forbidden", await CodeAsync(response));
        var record = await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.SingleAsync(a => a.Action == AuditActions.AuthorizationDenied && a.ActorId == userId));
        Assert.Contains(Permissions.Catalog.Read, record.Metadata);
    }

    [Fact]
    public async Task Admin_GetsPermissionCatalog()
    {
        var admin = await _factory.LoginAsAdminAsync();

        var body = await (await admin.GetAsync("/api/v1/permissions")).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(Permissions.All.Count, body.GetArrayLength());
    }

    [Fact]
    public async Task MustChangePassword_BlocksOtherEndpointsButNotMe()
    {
        var (_, client) = await UserAsync(mustChangePassword: true, SystemRoles.Admin);

        var blocked = await client.GetAsync("/api/v1/permissions");
        var me = await client.GetAsync("/api/v1/auth/me");

        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
        Assert.Equal("password_change_required", await CodeAsync(blocked));
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Fact]
    public async Task Sessions_IsBlockedWhilePasswordChangeRequired()
    {
        var (_, client) = await UserAsync(mustChangePassword: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/auth/sessions")).StatusCode);
    }

    [Fact]
    public async Task Health_IsAnonymous()
        => Assert.Equal(HttpStatusCode.OK, (await _factory.CreateHttpsClient().GetAsync("/health")).StatusCode);

    [Fact]
    public async Task RedisDown_LoginRefreshAndAuthorizedRequestsStillWork()
    {
        // Spec D11: Redis sập ⇒ hệ thống chậm hơn nhưng vẫn chạy (mỗi thao tác Redis chờ hết timeout 1s rồi rơi về DB).
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });
        var admin = await factory.LoginAsAdminAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/api/v1/auth/me")).StatusCode);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter AuthorizationPipelineTests`
Expected: FAIL — `/api/v1/permissions` 404.

- [ ] **Step 3: Danh mục quyền (Application)**

`PermissionCatalog/Models/PermissionDto.cs`:

```csharp
namespace CleanArchCqrs.Application.PermissionCatalog.Models;

public sealed record PermissionDto(string Code, string Group, string Description);
```

`PermissionCatalog/Queries/GetPermissions/GetPermissionsQuery.cs`:

```csharp
using CleanArchCqrs.Application.PermissionCatalog.Models;
using MediatR;

namespace CleanArchCqrs.Application.PermissionCatalog.Queries.GetPermissions;

public sealed record GetPermissionsQuery : IRequest<IReadOnlyList<PermissionDto>>;
```

`PermissionCatalog/Queries/GetPermissions/GetPermissionsQueryHandler.cs`:

```csharp
using CleanArchCqrs.Application.PermissionCatalog.Models;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.PermissionCatalog.Queries.GetPermissions;

/// Danh mục là code (seeder đồng bộ xuống DB) — đọc thẳng từ code, không cần DB.
public sealed class GetPermissionsQueryHandler : IRequestHandler<GetPermissionsQuery, IReadOnlyList<PermissionDto>>
{
    public Task<IReadOnlyList<PermissionDto>> Handle(GetPermissionsQuery request, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<PermissionDto>>(Permissions.All
            .OrderBy(p => p.Group).ThenBy(p => p.Code, StringComparer.Ordinal)
            .Select(p => new PermissionDto(p.Code, p.Group, p.Description))
            .ToList());
}
```

- [ ] **Step 4: Pipeline phân quyền (API)**

`Authorization/PermissionRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission) => Permission = permission;
}
```

`Authorization/PasswordChangeRequirement.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

/// Tài khoản MustChangePassword chỉ được gọi endpoint có [AllowWhilePasswordChangeRequired].
public sealed class PasswordChangeRequirement : IAuthorizationRequirement
{
}
```

`Authorization/HasPermissionAttribute.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

/// Dùng hằng trong Domain.Identity.Permissions, ví dụ [HasPermission(Permissions.Users.Read)].
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class HasPermissionAttribute : AuthorizeAttribute
{
    public HasPermissionAttribute(string permission) => Policy = PermissionPolicyProvider.PolicyPrefix + permission;
}
```

`Authorization/PermissionPolicyProvider.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace CleanArchCqrs.API.Authorization;

/// Sinh policy "perm:<mã>" động — không phải khai báo từng policy trong Program.cs.
public sealed class PermissionPolicyProvider : DefaultAuthorizationPolicyProvider
{
    public const string PolicyPrefix = "perm:";

    public PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : base(options)
    {
    }

    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PasswordChangeRequirement(), new PermissionRequirement(policyName[PolicyPrefix.Length..]))
            .Build();
    }
}
```

`Authorization/PermissionAuthorizationHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissions;

    public PermissionAuthorizationHandler(IPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)) return;
        var access = await _permissions.GetAsync(userId);
        if (access.Permissions.Contains(requirement.Permission)) context.Succeed(requirement);
    }
}
```

`Authorization/PasswordChangeRequirementHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.Authorization;

public sealed class PasswordChangeRequirementHandler : AuthorizationHandler<PasswordChangeRequirement>
{
    private readonly IPermissionService _permissions;

    public PasswordChangeRequirementHandler(IPermissionService permissions) => _permissions = permissions;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PasswordChangeRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirst("sub")?.Value, out var userId)) return;   // chưa đăng nhập: để RequireAuthenticatedUser xử lý

        if (context.Resource is HttpContext http
            && http.GetEndpoint()?.Metadata.GetMetadata<AllowWhilePasswordChangeRequiredAttribute>() is not null)
        {
            context.Succeed(requirement);
            return;
        }

        if ((await _permissions.GetAsync(userId)).MustChangePassword)
            context.Fail(new AuthorizationFailureReason(this, ErrorCodes.PasswordChangeRequired));
        else
            context.Succeed(requirement);
    }
}
```

`Authorization/ProblemAuthorizationResultHandler.cs`:

```csharp
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace CleanArchCqrs.API.Authorization;

/// Viết 403 dạng Problem Details và ghi audit authz.denied. 401 để JwtBearer OnChallenge xử lý.
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (!authorizeResult.Forbidden)
        {
            await _default.HandleAsync(next, context, policy, authorizeResult);
            return;
        }

        if (authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r.Message == ErrorCodes.PasswordChangeRequired) == true)
        {
            await ProblemResponseWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ErrorCodes.PasswordChangeRequired,
                "Bạn cần đổi mật khẩu trước khi tiếp tục.");
            return;
        }

        // Chưa có transaction nghiệp vụ nào ở bước authorize — lưu audit trong scope request là đủ.
        var required = policy.Requirements.OfType<PermissionRequirement>().Select(r => r.Permission).ToArray();
        context.RequestServices.GetRequiredService<IAuditRecorder>().Record(
            AuditActions.AuthorizationDenied, AuditResult.Denied, reason: "MissingPermission",
            metadata: new Dictionary<string, object?> { ["permissions"] = required, ["path"] = context.Request.Path.Value });
        await context.RequestServices.GetRequiredService<IUnitOfWork>().SaveChangesAsync(context.RequestAborted);

        await ProblemResponseWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ErrorCodes.Forbidden,
            "Bạn không có quyền thực hiện thao tác này.");
    }
}
```

`DependencyInjection/ApiAuthorizationExtensions.cs`:

```csharp
using CleanArchCqrs.API.Authorization;
using Microsoft.AspNetCore.Authorization;

namespace CleanArchCqrs.API.DependencyInjection;

public static class ApiAuthorizationExtensions
{
    public static IServiceCollection AddApiAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            var signedIn = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new PasswordChangeRequirement())
                .Build();
            options.DefaultPolicy = signedIn;
            options.FallbackPolicy = signedIn;   // từ chối mặc định: quên gắn attribute vẫn phải đăng nhập
        });
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, PasswordChangeRequirementHandler>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, ProblemAuthorizationResultHandler>();
        return services;
    }
}
```

`Controllers/PermissionsController.cs`:

```csharp
using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.Application.PermissionCatalog.Models;
using CleanArchCqrs.Application.PermissionCatalog.Queries.GetPermissions;
using CleanArchCqrs.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/permissions")]
public sealed class PermissionsController : ControllerBase
{
    private readonly ISender _mediator;

    public PermissionsController(ISender mediator) => _mediator = mediator;

    [HasPermission(Permissions.Catalog.Read)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PermissionDto>>> GetAll(CancellationToken ct)
        => Ok(await _mediator.Send(new GetPermissionsQuery(), ct));
}
```

`Program.cs`:
- ngay dưới `builder.Services.AddApiAuthentication();` thêm `builder.Services.AddApiAuthorization();`
- `app.MapHealthChecks("/health");` → `app.MapHealthChecks("/health").AllowAnonymous();`

- [ ] **Step 5: Chạy test, xác nhận xanh (kể cả test cũ)**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests`
Expected: toàn bộ xanh — `AuthorizationPipelineTests` 7/7; test Giai đoạn 3 (login/refresh/internal) vẫn xanh nhờ `[AllowAnonymous]`.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(authz): permission policies, deny-by-default fallback, forced password change, audited 403"
```

---

### Task 4.3: Quản trị tài khoản — danh sách, chi tiết, tạo, khoá/mở khoá

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Models/PagedResult.cs`
- Create: `src/CleanArchCqrs.Application/Users/Models/UserSummaryDto.cs`, `UserDetailDto.cs`, `PermissionGrantDto.cs`
- Create: `src/CleanArchCqrs.Application/Users/AdminSafety.cs`
- Create: `src/CleanArchCqrs.Application/Users/Queries/GetUsers/GetUsersQuery.cs`, `GetUsersQueryHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Queries/GetUser/GetUserQuery.cs`, `GetUserQueryHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Commands/CreateUser/CreateUserCommand.cs`, `CreateUserCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Commands/DeactivateUser/DeactivateUserCommand.cs`, `DeactivateUserCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Commands/ActivateUser/ActivateUserCommand.cs`, `ActivateUserCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Validators/GetUsersQueryValidator.cs`, `CreateUserCommandValidator.cs`
- Modify: `src/CleanArchCqrs.Application/Common/Interfaces/IIdentityReadService.cs`, `src/CleanArchCqrs.Infrastructure/Identity/IdentityReadService.cs`
- Create: `src/CleanArchCqrs.API/Controllers/UsersController.cs`
- Test: `tests/CleanArchCqrs.IntegrationTests/Admin/UsersAdminTests.cs`

**Interfaces:**
- Consumes: `IUserRepository` (+ `GetWithAccessAsync`, `CountActiveUsersInRoleAsync`), `IRoleRepository`, `ISessionRepository.GetActiveByUserForUpdateAsync`, `ICacheInvalidator`, `IAuditRecorder`, `PasswordRuleExtensions.NewPassword`, `PasswordPolicy`.
- Produces:
  - `record PagedResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount)`.
  - `record UserSummaryDto(Guid Id, string Email, string FullName, bool IsActive, IReadOnlyList<string> RoleCodes, DateTimeOffset? LastLoginAt)`; `record PermissionGrantDto(string Code, string Reason, DateTimeOffset GrantedAtUtc)`; `record UserDetailDto(Guid Id, string Email, string FullName, string? AvatarUrl, bool IsActive, bool MustChangePassword, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<PermissionGrantDto> PermissionGrants, DateTimeOffset CreatedAt, DateTimeOffset? LastLoginAt)`.
  - `IIdentityReadService` thêm: `Task<PagedResult<UserSummaryDto>> GetUsersAsync(int pageNumber, int pageSize, string? searchTerm, CancellationToken ct = default)` (lọc trước khi đếm, sắp theo Email rồi Id); `Task<UserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct = default)`.
  - `AdminSafety.GetAdminRoleAsync(IRoleRepository, CancellationToken)`, `AdminSafety.EnsureNotLastActiveAdminAsync(IUserRepository, Guid adminRoleId, User user, CancellationToken)` (ném 409 `last_admin`).
  - `record GetUsersQuery(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null)`, `record GetUserQuery(Guid UserId)`, `record CreateUserCommand(string Email, string FullName, string TemporaryPassword, IReadOnlyList<Guid> RoleIds) : IRequest<Guid>`, `record DeactivateUserCommand(Guid UserId) : IRequest`, `record ActivateUserCommand(Guid UserId) : IRequest`.
  - HTTP: `GET /api/v1/users` (`users.read`), `GET /api/v1/users/{id}` (`users.read`), `POST /api/v1/users` (`users.create`, 201), `POST /api/v1/users/{id}/deactivate|activate` (`users.activate`, CSRF, 204).

- [ ] **Step 1: Viết test (đỏ)**

`Admin/UsersAdminTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class UsersAdminTests : IAsyncLifetime
{
    private const string TempPassword = "Temp-Password-777";
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public UsersAdminTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private Task<Guid> RoleIdAsync(string code)
        => TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync());

    private async Task<HttpResponseMessage> CreateAsync(string email, string password = TempPassword, params Guid[] roleIds)
        => await _admin.SendAsync(HttpMethod.Post, "/api/v1/users",
            new { email, fullName = "Nguyễn Văn B", temporaryPassword = password, roleIds });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task Create_ThenSearchAndGetDetail()
    {
        var email = TestData.NewEmail("doctor");
        var created = await CreateAsync(email, TempPassword, await RoleIdAsync(SystemRoles.Doctor));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("id").GetString();

        var page = await JsonAsync(await _admin.GetAsync($"/api/v1/users?searchTerm={Uri.EscapeDataString(email)}"));
        var detail = await JsonAsync(await _admin.GetAsync($"/api/v1/users/{id}"));

        Assert.Equal(1, page.GetProperty("totalCount").GetInt32());
        Assert.Equal("doctor", page.GetProperty("items")[0].GetProperty("roleCodes")[0].GetString());
        Assert.True(detail.GetProperty("mustChangePassword").GetBoolean());
        Assert.Equal("doctor", detail.GetProperty("roles")[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreatedUser_LogsInWithTemporaryPasswordAndMustChangeIt()
    {
        var email = TestData.NewEmail();
        (await CreateAsync(email)).EnsureSuccessStatusCode();

        var user = new AuthTestClient(_factory.CreateHttpsClient());
        var login = await user.LoginAsync(email, TempPassword);

        Assert.True((await JsonAsync(login)).GetProperty("mustChangePassword").GetBoolean());
    }

    [Fact]
    public async Task Create_DuplicateEmail_Returns409()
    {
        var email = TestData.NewEmail();
        (await CreateAsync(email)).EnsureSuccessStatusCode();

        var again = await CreateAsync(email.ToUpperInvariant());

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("email_taken", (await JsonAsync(again)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_UnknownRoleOrWeakPassword_Returns400()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(TestData.NewEmail(), TempPassword, Guid.NewGuid())).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(TestData.NewEmail(), "short")).StatusCode);
    }

    [Fact]
    public async Task List_PageSizeOver100_Returns400()
        => Assert.Equal(HttpStatusCode.BadRequest, (await _admin.GetAsync("/api/v1/users?pageSize=101")).StatusCode);

    [Fact]
    public async Task GetUnknownUser_Returns404()
        => Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"/api/v1/users/{Guid.NewGuid()}")).StatusCode);

    [Fact]
    public async Task Deactivate_KillsSessionsBlocksLoginAndIsAudited_ActivateRestores()
    {
        var email = TestData.NewEmail();
        var userId = await TestData.CreateUserAsync(_factory, email);
        var user = new AuthTestClient(_factory.CreateHttpsClient());
        (await user.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();

        var response = await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/deactivate");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await user.RefreshAsync()).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, TestData.DefaultPassword)).StatusCode);
        Assert.True(await TestData.QueryAsync(_factory, db =>
            db.AuditRecords.AnyAsync(a => a.Action == AuditActions.UserDeactivate && a.ResourceId == userId.ToString())));

        (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{userId}/activate")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await new AuthTestClient(_factory.CreateHttpsClient()).LoginAsync(email, TestData.DefaultPassword)).StatusCode);
    }

    [Fact]
    public async Task Deactivate_Self_Returns409()
    {
        var me = await JsonAsync(await _admin.GetAsync("/api/v1/auth/me"));

        var response = await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{me.GetProperty("id").GetString()}/deactivate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("self_action_forbidden", (await JsonAsync(response)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Deactivate_LastActiveAdmin_Returns409()
    {
        var operatorEmail = TestData.NewEmail("operator");
        var operatorId = await TestData.CreateUserAsync(_factory, operatorEmail);
        await TestData.GrantAsync(_factory, operatorId, Permissions.Users.Activate);
        var operatorClient = new AuthTestClient(_factory.CreateHttpsClient());
        (await operatorClient.LoginAsync(operatorEmail, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        var adminId = await TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Email == _factory.AdminEmail).Select(u => u.Id).SingleAsync());

        var response = await operatorClient.SendAsync(HttpMethod.Post, $"/api/v1/users/{adminId}/deactivate");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last_admin", (await JsonAsync(response)).GetProperty("code").GetString());
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter UsersAdminTests`
Expected: FAIL — 404.

- [ ] **Step 3: Model, read service**

`Common/Models/PagedResult.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Models;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int PageNumber, int PageSize, int TotalCount);
```

`Users/Models/UserSummaryDto.cs`:

```csharp
namespace CleanArchCqrs.Application.Users.Models;

public sealed record UserSummaryDto(Guid Id, string Email, string FullName, bool IsActive,
    IReadOnlyList<string> RoleCodes, DateTimeOffset? LastLoginAt);
```

`Users/Models/PermissionGrantDto.cs`:

```csharp
namespace CleanArchCqrs.Application.Users.Models;

public sealed record PermissionGrantDto(string Code, string Reason, DateTimeOffset GrantedAtUtc);
```

`Users/Models/UserDetailDto.cs`:

```csharp
using CleanArchCqrs.Application.Auth.Models;

namespace CleanArchCqrs.Application.Users.Models;

public sealed record UserDetailDto(
    Guid Id,
    string Email,
    string FullName,
    string? AvatarUrl,
    bool IsActive,
    bool MustChangePassword,
    IReadOnlyList<RoleRefDto> Roles,
    IReadOnlyList<PermissionGrantDto> PermissionGrants,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt);
```

`IIdentityReadService.cs` — thêm `using CleanArchCqrs.Application.Common.Models;`, `using CleanArchCqrs.Application.Users.Models;` và:

```csharp
    Task<PagedResult<UserSummaryDto>> GetUsersAsync(int pageNumber, int pageSize, string? searchTerm, CancellationToken ct = default);
    Task<UserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct = default);
```

`IdentityReadService.cs` — thêm `using CleanArchCqrs.Application.Common.Models;`, `using CleanArchCqrs.Application.Users.Models;` và:

```csharp
    public async Task<PagedResult<UserSummaryDto>> GetUsersAsync(int pageNumber, int pageSize, string? searchTerm,
        CancellationToken ct = default)
    {
        var query = _db.Users.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var pattern = $"%{searchTerm.Trim()}%";
            query = query.Where(u => EF.Functions.ILike(u.Email, pattern) || EF.Functions.ILike(u.FullName, pattern));
        }

        var total = await query.CountAsync(ct);   // lọc trước, đếm sau (Đặc tả kỹ thuật §3.2)
        var rows = await query
            .OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Skip((pageNumber - 1) * pageSize).Take(pageSize)
            .Select(u => new { u.Id, u.Email, u.FullName, u.IsActive, u.LastLoginAt })
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Id).ToList();
        var roleCodes = await (
                from userRole in _db.Set<UserRole>()
                join role in _db.Roles on userRole.RoleId equals role.Id
                where ids.Contains(userRole.UserId)
                select new { userRole.UserId, role.Code })
            .ToListAsync(ct);

        var items = rows.Select(r => new UserSummaryDto(r.Id, r.Email, r.FullName, r.IsActive,
                roleCodes.Where(c => c.UserId == r.Id).Select(c => c.Code).Order(StringComparer.Ordinal).ToList(),
                r.LastLoginAt))
            .ToList();
        return new PagedResult<UserSummaryDto>(items, pageNumber, pageSize, total);
    }

    public async Task<UserDetailDto?> GetUserAsync(Guid userId, CancellationToken ct = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Id, u.Email, u.FullName, u.AvatarUrl, u.IsActive, u.MustChangePassword, u.CreatedAt, u.LastLoginAt })
            .SingleOrDefaultAsync(ct);
        if (user is null) return null;

        var grants = await _db.Set<UserPermission>().AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderBy(p => p.PermissionCode)
            .Select(p => new PermissionGrantDto(p.PermissionCode, p.Reason, p.GrantedAtUtc))
            .ToListAsync(ct);

        return new UserDetailDto(user.Id, user.Email, user.FullName, user.AvatarUrl, user.IsActive, user.MustChangePassword,
            await LoadRoleRefsAsync(userId, ct), grants, user.CreatedAt, user.LastLoginAt);
    }
```

- [ ] **Step 4: Query, command, validator**

`Users/AdminSafety.cs`:

```csharp
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Domain.Identity;

namespace CleanArchCqrs.Application.Users;

/// Quy tắc bảo vệ Spec §4.2: luôn còn ≥ 1 admin đang hoạt động.
internal static class AdminSafety
{
    public static async Task<Role> GetAdminRoleAsync(IRoleRepository roles, CancellationToken ct)
        => await roles.GetByCodeAsync(SystemRoles.Admin, ct)
           ?? throw new InvalidOperationException("The admin role has not been seeded.");

    /// Gọi TRƯỚC khi làm user mất quyền admin (khoá tài khoản hoặc gỡ role admin).
    public static async Task EnsureNotLastActiveAdminAsync(IUserRepository users, Guid adminRoleId, User user, CancellationToken ct)
    {
        if (!user.IsActive || !user.HasRole(adminRoleId)) return;
        if (await users.CountActiveUsersInRoleAsync(adminRoleId, excludingUserId: user.Id, ct) == 0)
            throw new ConflictException(ErrorCodes.LastAdmin, "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.");
    }
}
```

`Users/Queries/GetUsers/GetUsersQuery.cs`:

```csharp
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Models;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUsers;

public sealed record GetUsersQuery(int PageNumber = 1, int PageSize = 20, string? SearchTerm = null)
    : IRequest<PagedResult<UserSummaryDto>>;
```

`Users/Queries/GetUsers/GetUsersQueryHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Models;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUsers;

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, PagedResult<UserSummaryDto>>
{
    private readonly IIdentityReadService _read;

    public GetUsersQueryHandler(IIdentityReadService read) => _read = read;

    public Task<PagedResult<UserSummaryDto>> Handle(GetUsersQuery request, CancellationToken ct)
        => _read.GetUsersAsync(request.PageNumber, request.PageSize, request.SearchTerm, ct);
}
```

`Users/Validators/GetUsersQueryValidator.cs`:

```csharp
using CleanArchCqrs.Application.Users.Queries.GetUsers;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class GetUsersQueryValidator : AbstractValidator<GetUsersQuery>
{
    public GetUsersQueryValidator()
    {
        RuleFor(x => x.PageNumber).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
        RuleFor(x => x.SearchTerm).MaximumLength(100);
    }
}
```

`Users/Queries/GetUser/GetUserQuery.cs`:

```csharp
using CleanArchCqrs.Application.Users.Models;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUser;

public sealed record GetUserQuery(Guid UserId) : IRequest<UserDetailDto>;
```

`Users/Queries/GetUser/GetUserQueryHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Users.Models;
using CleanArchCqrs.Domain.Exceptions;
using MediatR;

namespace CleanArchCqrs.Application.Users.Queries.GetUser;

public sealed class GetUserQueryHandler : IRequestHandler<GetUserQuery, UserDetailDto>
{
    private readonly IIdentityReadService _read;

    public GetUserQueryHandler(IIdentityReadService read) => _read = read;

    public async Task<UserDetailDto> Handle(GetUserQuery request, CancellationToken ct)
        => await _read.GetUserAsync(request.UserId, ct)
           ?? throw new NotFoundException($"User '{request.UserId}' was not found.");
}
```

`Users/Commands/CreateUser/CreateUserCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.CreateUser;

public sealed record CreateUserCommand(string Email, string FullName, string TemporaryPassword, IReadOnlyList<Guid> RoleIds)
    : IRequest<Guid>;
```

`Users/Validators/CreateUserCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Common.Security;
using CleanArchCqrs.Application.Users.Commands.CreateUser;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.TemporaryPassword).NewPassword()
            .Must((command, password) => !PasswordPolicy.ContainsEmailLocalPart(password, command.Email))
            .WithMessage("Mật khẩu không được chứa phần tên trong email.");
        RuleFor(x => x.RoleIds).NotNull();
    }
}
```

`Users/Commands/CreateUser/CreateUserCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.CreateUser;

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public CreateUserCommandHandler(IUserRepository users, IRoleRepository roles, IPasswordHasher passwordHasher,
        ICurrentUser currentUser, IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _users = users;
        _roles = roles;
        _passwordHasher = passwordHasher;
        _currentUser = currentUser;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken ct)
    {
        if (await _users.EmailExistsAsync(request.Email, ct))
            throw new ConflictException(ErrorCodes.EmailTaken, "Email đã được dùng cho tài khoản khác.");

        var roleIds = request.RoleIds.Distinct().ToList();
        if ((await _roles.GetByIdsAsync(roleIds, ct)).Count != roleIds.Count)
            throw new ValidationException(nameof(request.RoleIds), "Có vai trò không tồn tại.");

        var user = User.Create(request.FullName, request.Email, _passwordHasher.Hash(request.TemporaryPassword), avatarUrl: null);
        user.SetRoles(roleIds, _currentUser.UserId, _time.GetUtcNow());
        await _users.AddUserAsync(user, ct);
        _audit.Record(AuditActions.UserCreate, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["roleIds"] = roleIds });
        await _unitOfWork.SaveChangesAsync(ct);
        return user.Id;
    }
}
```

`Users/Commands/DeactivateUser/DeactivateUserCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.DeactivateUser;

public sealed record DeactivateUserCommand(Guid UserId) : IRequest;
```

`Users/Commands/DeactivateUser/DeactivateUserCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Domain.Identity.Sessions;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.DeactivateUser;

/// Khoá tài khoản: SecurityVersion++ và thu hồi mọi phiên trong cùng khoá hàng (Đặc tả kỹ thuật §4.3).
public sealed class DeactivateUserCommandHandler : IRequestHandler<DeactivateUserCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ISessionRepository _sessions;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public DeactivateUserCommandHandler(ICurrentUser currentUser, IUserRepository users, IRoleRepository roles,
        ISessionRepository sessions, ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork,
        TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _roles = roles;
        _sessions = sessions;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(DeactivateUserCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        if (request.UserId == actorId)
            throw new ConflictException(ErrorCodes.SelfActionForbidden, "Không thể tự khoá tài khoản của chính mình.");

        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");
        if (!user.IsActive) return;
        var adminRole = await AdminSafety.GetAdminRoleAsync(_roles, ct);
        await AdminSafety.EnsureNotLastActiveAdminAsync(_users, adminRole.Id, user, ct);

        var now = _time.GetUtcNow();
        await using var transaction = await _unitOfWork.BeginTransactionAsync(ct);
        foreach (var family in await _sessions.GetActiveByUserForUpdateAsync(user.Id, ct))
        {
            family.Revoke(SessionRevokeReason.AccountDeactivated, now);
            _cacheInvalidator.InvalidateSession(family.Id);
        }
        user.Deactivate();
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserDeactivate, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

`Users/Commands/ActivateUser/ActivateUserCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.ActivateUser;

public sealed record ActivateUserCommand(Guid UserId) : IRequest;
```

`Users/Commands/ActivateUser/ActivateUserCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.ActivateUser;

public sealed class ActivateUserCommandHandler : IRequestHandler<ActivateUserCommand>
{
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public ActivateUserCommandHandler(IUserRepository users, ICacheInvalidator cacheInvalidator, IAuditRecorder audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(ActivateUserCommand request, CancellationToken ct)
    {
        var user = await _users.GetUserByIdAsync(request.UserId, ct);   // NotFoundException ⇒ 404
        if (user.IsActive) return;

        user.Activate();
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserActivate, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

- [ ] **Step 5: Controller**

`Controllers/UsersController.cs`:

```csharp
using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Application.Users.Commands.ActivateUser;
using CleanArchCqrs.Application.Users.Commands.CreateUser;
using CleanArchCqrs.Application.Users.Commands.DeactivateUser;
using CleanArchCqrs.Application.Users.Models;
using CleanArchCqrs.Application.Users.Queries.GetUser;
using CleanArchCqrs.Application.Users.Queries.GetUsers;
using CleanArchCqrs.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/users")]
public sealed class UsersController : ControllerBase
{
    private readonly ISender _mediator;

    public UsersController(ISender mediator) => _mediator = mediator;

    [HasPermission(Permissions.Users.Read)]
    [HttpGet]
    public async Task<ActionResult<PagedResult<UserSummaryDto>>> List([FromQuery] GetUsersQuery query, CancellationToken ct)
        => Ok(await _mediator.Send(query, ct));

    [HasPermission(Permissions.Users.Read)]
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserDetailDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _mediator.Send(new GetUserQuery(id), ct));

    [HasPermission(Permissions.Users.Create)]
    [CsrfProtected]
    [HttpPost]
    public async Task<IActionResult> Create(CreateUserCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(Get), new { id }, new { id });
    }

    [HasPermission(Permissions.Users.Activate)]
    [CsrfProtected]
    [HttpPost("{id:guid}/deactivate")]
    public async Task<IActionResult> Deactivate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new DeactivateUserCommand(id), ct);
        return NoContent();
    }

    [HasPermission(Permissions.Users.Activate)]
    [CsrfProtected]
    [HttpPost("{id:guid}/activate")]
    public async Task<IActionResult> Activate(Guid id, CancellationToken ct)
    {
        await _mediator.Send(new ActivateUserCommand(id), ct);
        return NoContent();
    }
}
```

⚠ `[CsrfProtected]` cũng áp cho command quản trị: token nằm trong RAM nên CSRF khó xảy ra, nhưng Đặc tả §4.1 yêu cầu command đổi dữ liệu có kiểm Origin + CSRF — giữ nhất quán.

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter UsersAdminTests`
Expected: PASS 9/9.

- [ ] **Step 7: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(users): admin can list, view, create, deactivate and activate accounts with last-admin guard"
```

---

### Task 4.4: Gán role, cấp/thu hồi quyền lẻ — hiệu lực ngay

**Files:**
- Create: `src/CleanArchCqrs.Application/Users/Commands/SetUserRoles/SetUserRolesCommand.cs`, `SetUserRolesCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Commands/GrantUserPermission/GrantUserPermissionCommand.cs`, `GrantUserPermissionCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Commands/RevokeUserPermission/RevokeUserPermissionCommand.cs`, `RevokeUserPermissionCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Users/Validators/SetUserRolesCommandValidator.cs`, `GrantUserPermissionCommandValidator.cs`, `RevokeUserPermissionCommandValidator.cs`
- Create: `src/CleanArchCqrs.API/Contracts/Users/SetUserRolesRequest.cs`, `PermissionGrantRequest.cs`
- Modify: `src/CleanArchCqrs.API/Controllers/UsersController.cs`
- Test: `tests/CleanArchCqrs.IntegrationTests/Admin/UserAccessManagementTests.cs`

**Interfaces:**
- Consumes: `User.SetRoles/GrantPermission/RevokePermission` (2.3), `AdminSafety` (4.3), `ICacheInvalidator.InvalidatePermissions`.
- Produces:
  - `record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : IRequest` — tự gỡ admin của mình ⇒ 409 `self_action_forbidden`; gỡ admin cuối ⇒ 409 `last_admin`; role không tồn tại ⇒ 400.
  - `record GrantUserPermissionCommand(Guid UserId, string PermissionCode, string Reason) : IRequest`; `record RevokeUserPermissionCommand(Guid UserId, string PermissionCode, string Reason) : IRequest` — mã ngoài danh mục ⇒ 400.
  - `record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds)`; `record PermissionGrantRequest(string PermissionCode, string Reason)`.
  - HTTP: `PUT /api/v1/users/{id}/roles` (`users.roles.manage`), `POST /api/v1/users/{id}/permissions/grant|revoke` (`users.permissions.manage`); tất cả CSRF, 204.

- [ ] **Step 1: Viết test (đỏ)**

`Admin/UserAccessManagementTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class UserAccessManagementTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public UserAccessManagementTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(Guid Id, AuthTestClient Client)> StaffAsync()
    {
        var email = TestData.NewEmail("staff");
        var id = await TestData.CreateUserAsync(_factory, email);
        var client = new AuthTestClient(_factory.CreateHttpsClient());
        (await client.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        return (id, client);
    }

    private Task<Guid> RoleIdAsync(string code)
        => TestData.QueryAsync(_factory, db => db.Roles.Where(r => r.Code == code).Select(r => r.Id).SingleAsync());

    private Task<Guid> SeededAdminIdAsync()
        => TestData.QueryAsync(_factory, db => db.Users.Where(u => u.Email == _factory.AdminEmail).Select(u => u.Id).SingleAsync());

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
        => (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString();

    [Fact]
    public async Task GrantThenRevoke_TakesEffectOnTheVeryNextRequest()
    {
        var (staffId, staff) = await StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/permissions")).StatusCode);   // cache đã ấm

        (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{staffId}/permissions/grant",
            new { permissionCode = Permissions.Catalog.Read, reason = "Hỗ trợ cấu hình" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/permissions")).StatusCode);

        (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{staffId}/permissions/revoke",
            new { permissionCode = Permissions.Catalog.Read, reason = "Hết nhu cầu" })).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/permissions")).StatusCode);

        Assert.True(await TestData.QueryAsync(_factory, db => db.AuditRecords.AnyAsync(a =>
            a.Action == AuditActions.UserPermissionGrant && a.ResourceId == staffId.ToString())));
    }

    [Fact]
    public async Task SetRoles_TakesEffectOnTheVeryNextRequest()
    {
        var (staffId, staff) = await StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/v1/users")).StatusCode);

        var response = await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{staffId}/roles",
            new { roleIds = new[] { await RoleIdAsync(SystemRoles.Admin) } });

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/v1/users")).StatusCode);
    }

    [Fact]
    public async Task RemovingOwnAdminRole_Returns409()
    {
        var response = await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{await SeededAdminIdAsync()}/roles",
            new { roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("self_action_forbidden", await CodeAsync(response));
    }

    [Fact]
    public async Task RemovingLastAdminRole_Returns409()
    {
        var (operatorId, operatorClient) = await StaffAsync();
        await TestData.GrantAsync(_factory, operatorId, Permissions.Users.ManageRoles);

        var response = await operatorClient.SendAsync(HttpMethod.Put, $"/api/v1/users/{await SeededAdminIdAsync()}/roles",
            new { roleIds = Array.Empty<Guid>() });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("last_admin", await CodeAsync(response));
    }

    [Fact]
    public async Task UnknownPermissionOrRole_Returns400()
    {
        var (staffId, _) = await StaffAsync();

        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Post, $"/api/v1/users/{staffId}/permissions/grant",
            new { permissionCode = "patients.read", reason = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{staffId}/roles",
            new { roleIds = new[] { Guid.NewGuid() } })).StatusCode);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter UserAccessManagementTests`
Expected: FAIL — 404/405.

- [ ] **Step 3: Command, validator**

`Users/Commands/SetUserRoles/SetUserRolesCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.SetUserRoles;

public sealed record SetUserRolesCommand(Guid UserId, IReadOnlyList<Guid> RoleIds) : IRequest;
```

`Users/Validators/SetUserRolesCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Users.Commands.SetUserRoles;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class SetUserRolesCommandValidator : AbstractValidator<SetUserRolesCommand>
{
    public SetUserRolesCommandValidator() => RuleFor(x => x.RoleIds).NotNull();
}
```

`Users/Commands/SetUserRoles/SetUserRolesCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Auth;
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.SetUserRoles;

public sealed class SetUserRolesCommandHandler : IRequestHandler<SetUserRolesCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly IRoleRepository _roles;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public SetUserRolesCommandHandler(ICurrentUser currentUser, IUserRepository users, IRoleRepository roles,
        ICacheInvalidator cacheInvalidator, IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _roles = roles;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(SetUserRolesCommand request, CancellationToken ct)
    {
        var actorId = _currentUser.UserId ?? throw new UnauthorizedException(AuthMessages.SessionInvalid);
        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");

        var roleIds = request.RoleIds.Distinct().ToList();
        if ((await _roles.GetByIdsAsync(roleIds, ct)).Count != roleIds.Count)
            throw new ValidationException(nameof(request.RoleIds), "Có vai trò không tồn tại.");

        var adminRole = await AdminSafety.GetAdminRoleAsync(_roles, ct);
        if (user.HasRole(adminRole.Id) && !roleIds.Contains(adminRole.Id))
        {
            if (user.Id == actorId)
                throw new ConflictException(ErrorCodes.SelfActionForbidden, "Không thể tự gỡ vai trò quản trị của chính mình.");
            await AdminSafety.EnsureNotLastActiveAdminAsync(_users, adminRole.Id, user, ct);
        }

        user.SetRoles(roleIds, actorId, _time.GetUtcNow());
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserRolesSet, AuditResult.Succeeded, resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["roleIds"] = roleIds });
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

`Users/Commands/GrantUserPermission/GrantUserPermissionCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.GrantUserPermission;

public sealed record GrantUserPermissionCommand(Guid UserId, string PermissionCode, string Reason) : IRequest;
```

`Users/Validators/GrantUserPermissionCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Users.Commands.GrantUserPermission;
using CleanArchCqrs.Domain.Identity;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class GrantUserPermissionCommandValidator : AbstractValidator<GrantUserPermissionCommand>
{
    public GrantUserPermissionCommandValidator()
    {
        RuleFor(x => x.PermissionCode).NotEmpty().Must(Permissions.IsDefined).WithMessage("Quyền không có trong danh mục.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
```

`Users/Commands/GrantUserPermission/GrantUserPermissionCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.GrantUserPermission;

public sealed class GrantUserPermissionCommandHandler : IRequestHandler<GrantUserPermissionCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly TimeProvider _time;

    public GrantUserPermissionCommandHandler(ICurrentUser currentUser, IUserRepository users, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork, TimeProvider time)
    {
        _currentUser = currentUser;
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _time = time;
    }

    public async Task Handle(GrantUserPermissionCommand request, CancellationToken ct)
    {
        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");

        user.GrantPermission(request.PermissionCode, request.Reason, _currentUser.UserId, _time.GetUtcNow());
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserPermissionGrant, AuditResult.Succeeded, reason: request.Reason,
            resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["permission"] = request.PermissionCode });
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

`Users/Commands/RevokeUserPermission/RevokeUserPermissionCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.RevokeUserPermission;

public sealed record RevokeUserPermissionCommand(Guid UserId, string PermissionCode, string Reason) : IRequest;
```

`Users/Validators/RevokeUserPermissionCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Users.Commands.RevokeUserPermission;
using CleanArchCqrs.Domain.Identity;
using FluentValidation;

namespace CleanArchCqrs.Application.Users.Validators;

public sealed class RevokeUserPermissionCommandValidator : AbstractValidator<RevokeUserPermissionCommand>
{
    public RevokeUserPermissionCommandValidator()
    {
        RuleFor(x => x.PermissionCode).NotEmpty().Must(Permissions.IsDefined).WithMessage("Quyền không có trong danh mục.");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500);
    }
}
```

`Users/Commands/RevokeUserPermission/RevokeUserPermissionCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Users.Commands.RevokeUserPermission;

public sealed class RevokeUserPermissionCommandHandler : IRequestHandler<RevokeUserPermissionCommand>
{
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RevokeUserPermissionCommandHandler(IUserRepository users, ICacheInvalidator cacheInvalidator, IAuditRecorder audit,
        IUnitOfWork unitOfWork)
    {
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(RevokeUserPermissionCommand request, CancellationToken ct)
    {
        var user = await _users.GetWithAccessAsync(request.UserId, ct)
                   ?? throw new NotFoundException($"User '{request.UserId}' was not found.");

        user.RevokePermission(request.PermissionCode);
        _cacheInvalidator.InvalidatePermissions(user.Id);
        _audit.Record(AuditActions.UserPermissionRevoke, AuditResult.Succeeded, reason: request.Reason,
            resourceType: nameof(User), resourceId: user.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["permission"] = request.PermissionCode });
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

- [ ] **Step 4: API**

`Contracts/Users/SetUserRolesRequest.cs`:

```csharp
namespace CleanArchCqrs.API.Contracts.Users;

public sealed record SetUserRolesRequest(IReadOnlyList<Guid> RoleIds);
```

`Contracts/Users/PermissionGrantRequest.cs`:

```csharp
namespace CleanArchCqrs.API.Contracts.Users;

public sealed record PermissionGrantRequest(string PermissionCode, string Reason);
```

`UsersController.cs` — thêm using `CleanArchCqrs.API.Contracts.Users`, `CleanArchCqrs.Application.Users.Commands.SetUserRoles`, `...GrantUserPermission`, `...RevokeUserPermission` và:

```csharp
    [HasPermission(Permissions.Users.ManageRoles)]
    [CsrfProtected]
    [HttpPut("{id:guid}/roles")]
    public async Task<IActionResult> SetRoles(Guid id, SetUserRolesRequest request, CancellationToken ct)
    {
        await _mediator.Send(new SetUserRolesCommand(id, request.RoleIds), ct);
        return NoContent();
    }

    [HasPermission(Permissions.Users.ManagePermissions)]
    [CsrfProtected]
    [HttpPost("{id:guid}/permissions/grant")]
    public async Task<IActionResult> GrantPermission(Guid id, PermissionGrantRequest request, CancellationToken ct)
    {
        await _mediator.Send(new GrantUserPermissionCommand(id, request.PermissionCode, request.Reason), ct);
        return NoContent();
    }

    [HasPermission(Permissions.Users.ManagePermissions)]
    [CsrfProtected]
    [HttpPost("{id:guid}/permissions/revoke")]
    public async Task<IActionResult> RevokePermission(Guid id, PermissionGrantRequest request, CancellationToken ct)
    {
        await _mediator.Send(new RevokeUserPermissionCommand(id, request.PermissionCode, request.Reason), ct);
        return NoContent();
    }
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter UserAccessManagementTests`
Expected: PASS 5/5.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(users): assign roles and grant/revoke permissions with immediate cache invalidation"
```

---

### Task 4.5: Quản trị vai trò

**Files:**
- Create: `src/CleanArchCqrs.Application/Roles/Models/RoleDto.cs`
- Create: `src/CleanArchCqrs.Application/Roles/Queries/GetRoles/GetRolesQuery.cs`, `GetRolesQueryHandler.cs`
- Create: `src/CleanArchCqrs.Application/Roles/Commands/CreateRole/CreateRoleCommand.cs`, `CreateRoleCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Roles/Commands/RenameRole/RenameRoleCommand.cs`, `RenameRoleCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Roles/Commands/SetRolePermissions/SetRolePermissionsCommand.cs`, `SetRolePermissionsCommandHandler.cs`
- Create: `src/CleanArchCqrs.Application/Roles/Validators/CreateRoleCommandValidator.cs`, `RenameRoleCommandValidator.cs`, `SetRolePermissionsCommandValidator.cs`
- Modify: `IIdentityReadService.cs`, `IdentityReadService.cs`
- Create: `src/CleanArchCqrs.API/Contracts/Roles/RenameRoleRequest.cs`, `SetRolePermissionsRequest.cs`
- Create: `src/CleanArchCqrs.API/Controllers/RolesController.cs`
- Test: `tests/CleanArchCqrs.IntegrationTests/Admin/RolesAdminTests.cs`

**Interfaces:**
- Consumes: `Role.Create/Rename/SetPermissions/IsValidCode` (2.2), `IRoleRepository`, `IUserRepository.GetUserIdsInRoleAsync` (2.7).
- Produces:
  - `record RoleDto(Guid Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions)`; `IIdentityReadService.GetRolesAsync(CancellationToken ct = default) : Task<IReadOnlyList<RoleDto>>`.
  - `GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>`; `record CreateRoleCommand(string Code, string Name, IReadOnlyList<string> PermissionCodes) : IRequest<Guid>` (mã trùng ⇒ 409 `conflict`); `record RenameRoleCommand(Guid RoleId, string Name) : IRequest`; `record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> PermissionCodes) : IRequest` — xoá `perm:{uid}` của **mọi user thuộc role**.
  - HTTP: `GET /api/v1/roles` (`roles.read`), `POST /api/v1/roles` (`roles.manage`, 201), `PUT /api/v1/roles/{id}` (`roles.manage`), `PUT /api/v1/roles/{id}/permissions` (`roles.manage`).

- [ ] **Step 1: Viết test (đỏ)**

`Admin/RolesAdminTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.IntegrationTests.Helpers;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Admin;

[Collection(IntegrationCollection.Name)]
public class RolesAdminTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ApiFactory _factory = default!;
    private AuthTestClient _admin = default!;

    public RolesAdminTests(ContainersFixture containers) => _containers = containers;

    public async Task InitializeAsync()
    {
        _factory = await ApiFactory.CreateAsync(_containers);
        _admin = await _factory.LoginAsAdminAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    private async Task<HttpResponseMessage> CreateRoleAsync(string code, params string[] permissions)
        => await _admin.SendAsync(HttpMethod.Post, "/api/v1/roles", new { code, name = "Kiểm toán", permissionCodes = permissions });

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
        => await response.Content.ReadFromJsonAsync<JsonElement>();

    [Fact]
    public async Task List_ContainsNineSystemRoles()
    {
        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));

        Assert.Equal(9, roles.EnumerateArray().Count(r => r.GetProperty("isSystem").GetBoolean()));
    }

    [Fact]
    public async Task Create_ValidatesCodeAndRejectsDuplicates()
    {
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("Not Kebab")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await CreateRoleAsync("auditor-x", "patients.read")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await CreateRoleAsync("auditor")).StatusCode);

        var duplicate = await CreateRoleAsync("auditor");

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("conflict", (await JsonAsync(duplicate)).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Rename_ChangesName()
    {
        var id = (await JsonAsync(await CreateRoleAsync("renamer"))).GetProperty("id").GetString();

        (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{id}", new { name = "Tên mới" })).EnsureSuccessStatusCode();

        var roles = await JsonAsync(await _admin.GetAsync("/api/v1/roles"));
        Assert.Equal("Tên mới", roles.EnumerateArray().Single(r => r.GetProperty("id").GetString() == id).GetProperty("name").GetString());
    }

    [Fact]
    public async Task SetRolePermissions_AffectsEveryMemberOnNextRequest()
    {
        var roleId = (await JsonAsync(await CreateRoleAsync("catalog-viewer"))).GetProperty("id").GetGuid();
        var email = TestData.NewEmail();
        var memberId = await TestData.CreateUserAsync(_factory, email);
        (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/users/{memberId}/roles", new { roleIds = new[] { roleId } })).EnsureSuccessStatusCode();
        var member = new AuthTestClient(_factory.CreateHttpsClient());
        (await member.LoginAsync(email, TestData.DefaultPassword)).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await member.GetAsync("/api/v1/permissions")).StatusCode);

        (await _admin.SendAsync(HttpMethod.Put, $"/api/v1/roles/{roleId}/permissions",
            new { permissionCodes = new[] { Permissions.Catalog.Read } })).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync("/api/v1/permissions")).StatusCode);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter RolesAdminTests`
Expected: FAIL — 404.

- [ ] **Step 3: Application**

`Roles/Models/RoleDto.cs`:

```csharp
namespace CleanArchCqrs.Application.Roles.Models;

public sealed record RoleDto(Guid Id, string Code, string Name, bool IsSystem, IReadOnlyList<string> Permissions);
```

`IIdentityReadService.cs` — thêm `using CleanArchCqrs.Application.Roles.Models;` và:

```csharp
    Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default);
```

`IdentityReadService.cs` — thêm `using CleanArchCqrs.Application.Roles.Models;` và:

```csharp
    public async Task<IReadOnlyList<RoleDto>> GetRolesAsync(CancellationToken ct = default)
    {
        var roles = await _db.Roles.AsNoTracking().Include(r => r.GrantedPermissions).OrderBy(r => r.Code).ToListAsync(ct);
        return roles.Select(r => new RoleDto(r.Id, r.Code, r.Name, r.IsSystem,
                r.GrantedPermissions.Select(p => p.PermissionCode).Order(StringComparer.Ordinal).ToList()))
            .ToList();
    }
```

`Roles/Queries/GetRoles/GetRolesQuery.cs`:

```csharp
using CleanArchCqrs.Application.Roles.Models;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Queries.GetRoles;

public sealed record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;
```

`Roles/Queries/GetRoles/GetRolesQueryHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Roles.Models;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Queries.GetRoles;

public sealed class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IIdentityReadService _read;

    public GetRolesQueryHandler(IIdentityReadService read) => _read = read;

    public Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct) => _read.GetRolesAsync(ct);
}
```

`Roles/Commands/CreateRole/CreateRoleCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.CreateRole;

public sealed record CreateRoleCommand(string Code, string Name, IReadOnlyList<string> PermissionCodes) : IRequest<Guid>;
```

`Roles/Validators/CreateRoleCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Roles.Commands.CreateRole;
using CleanArchCqrs.Domain.Identity;
using FluentValidation;

namespace CleanArchCqrs.Application.Roles.Validators;

public sealed class CreateRoleCommandValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleCommandValidator()
    {
        RuleFor(x => x.Code).Must(Role.IsValidCode).WithMessage("Mã vai trò chỉ gồm chữ thường, số, gạch nối; 2–50 ký tự.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PermissionCodes).NotNull();
        RuleForEach(x => x.PermissionCodes).Must(Permissions.IsDefined).WithMessage("Quyền không có trong danh mục.");
    }
}
```

`Roles/Commands/CreateRole/CreateRoleCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.CreateRole;

public sealed class CreateRoleCommandHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IRoleRepository _roles;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public CreateRoleCommandHandler(IRoleRepository roles, IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        if (await _roles.CodeExistsAsync(request.Code, ct))
            throw new ConflictException(ErrorCodes.Conflict, "Mã vai trò đã tồn tại.");

        var role = Role.Create(request.Code, request.Name);
        role.SetPermissions(request.PermissionCodes);
        await _roles.AddAsync(role, ct);
        _audit.Record(AuditActions.RoleCreate, AuditResult.Succeeded, resourceType: nameof(Role), resourceId: role.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
        return role.Id;
    }
}
```

`Roles/Commands/RenameRole/RenameRoleCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.RenameRole;

public sealed record RenameRoleCommand(Guid RoleId, string Name) : IRequest;
```

`Roles/Validators/RenameRoleCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Roles.Commands.RenameRole;
using FluentValidation;

namespace CleanArchCqrs.Application.Roles.Validators;

public sealed class RenameRoleCommandValidator : AbstractValidator<RenameRoleCommand>
{
    public RenameRoleCommandValidator() => RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
}
```

`Roles/Commands/RenameRole/RenameRoleCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.RenameRole;

public sealed class RenameRoleCommandHandler : IRequestHandler<RenameRoleCommand>
{
    private readonly IRoleRepository _roles;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public RenameRoleCommandHandler(IRoleRepository roles, IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(RenameRoleCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct)
                   ?? throw new NotFoundException($"Role '{request.RoleId}' was not found.");
        role.Rename(request.Name);   // Code không đổi được — kể cả role hệ thống vẫn đổi được tên hiển thị
        _audit.Record(AuditActions.RoleUpdate, AuditResult.Succeeded, resourceType: nameof(Role), resourceId: role.Id.ToString());
        await _unitOfWork.SaveChangesAsync(ct);
    }
}
```

`Roles/Commands/SetRolePermissions/SetRolePermissionsCommand.cs`:

```csharp
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;

public sealed record SetRolePermissionsCommand(Guid RoleId, IReadOnlyList<string> PermissionCodes) : IRequest;
```

`Roles/Validators/SetRolePermissionsCommandValidator.cs`:

```csharp
using CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;
using CleanArchCqrs.Domain.Identity;
using FluentValidation;

namespace CleanArchCqrs.Application.Roles.Validators;

public sealed class SetRolePermissionsCommandValidator : AbstractValidator<SetRolePermissionsCommand>
{
    public SetRolePermissionsCommandValidator()
    {
        RuleFor(x => x.PermissionCodes).NotNull();
        RuleForEach(x => x.PermissionCodes).Must(Permissions.IsDefined).WithMessage("Quyền không có trong danh mục.");
    }
}
```

`Roles/Commands/SetRolePermissions/SetRolePermissionsCommandHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Auditing;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using MediatR;

namespace CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;

public sealed class SetRolePermissionsCommandHandler : IRequestHandler<SetRolePermissionsCommand>
{
    private readonly IRoleRepository _roles;
    private readonly IUserRepository _users;
    private readonly ICacheInvalidator _cacheInvalidator;
    private readonly IAuditRecorder _audit;
    private readonly IUnitOfWork _unitOfWork;

    public SetRolePermissionsCommandHandler(IRoleRepository roles, IUserRepository users, ICacheInvalidator cacheInvalidator,
        IAuditRecorder audit, IUnitOfWork unitOfWork)
    {
        _roles = roles;
        _users = users;
        _cacheInvalidator = cacheInvalidator;
        _audit = audit;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(SetRolePermissionsCommand request, CancellationToken ct)
    {
        var role = await _roles.GetByIdAsync(request.RoleId, ct)
                   ?? throw new NotFoundException($"Role '{request.RoleId}' was not found.");

        role.SetPermissions(request.PermissionCodes);
        foreach (var userId in await _users.GetUserIdsInRoleAsync(role.Id, ct))
            _cacheInvalidator.InvalidatePermissions(userId);
        _audit.Record(AuditActions.RolePermissionsSet, AuditResult.Succeeded, resourceType: nameof(Role), resourceId: role.Id.ToString(),
            metadata: new Dictionary<string, object?> { ["permissions"] = request.PermissionCodes });
        await _unitOfWork.SaveChangesAsync(ct);
        await _cacheInvalidator.FlushAsync(ct);
    }
}
```

- [ ] **Step 4: API**

`Contracts/Roles/RenameRoleRequest.cs`:

```csharp
namespace CleanArchCqrs.API.Contracts.Roles;

public sealed record RenameRoleRequest(string Name);
```

`Contracts/Roles/SetRolePermissionsRequest.cs`:

```csharp
namespace CleanArchCqrs.API.Contracts.Roles;

public sealed record SetRolePermissionsRequest(IReadOnlyList<string> PermissionCodes);
```

`Controllers/RolesController.cs`:

```csharp
using CleanArchCqrs.API.Auth;
using CleanArchCqrs.API.Authorization;
using CleanArchCqrs.API.Contracts.Roles;
using CleanArchCqrs.Application.Roles.Commands.CreateRole;
using CleanArchCqrs.Application.Roles.Commands.RenameRole;
using CleanArchCqrs.Application.Roles.Commands.SetRolePermissions;
using CleanArchCqrs.Application.Roles.Models;
using CleanArchCqrs.Application.Roles.Queries.GetRoles;
using CleanArchCqrs.Domain.Identity;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Controllers;

[ApiController]
[Route("api/v1/roles")]
public sealed class RolesController : ControllerBase
{
    private readonly ISender _mediator;

    public RolesController(ISender mediator) => _mediator = mediator;

    [HasPermission(Permissions.Roles.Read)]
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct)
        => Ok(await _mediator.Send(new GetRolesQuery(), ct));

    [HasPermission(Permissions.Roles.Manage)]
    [CsrfProtected]
    [HttpPost]
    public async Task<IActionResult> Create(CreateRoleCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return Created($"/api/v1/roles/{id}", new { id });
    }

    [HasPermission(Permissions.Roles.Manage)]
    [CsrfProtected]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Rename(Guid id, RenameRoleRequest request, CancellationToken ct)
    {
        await _mediator.Send(new RenameRoleCommand(id, request.Name), ct);
        return NoContent();
    }

    [HasPermission(Permissions.Roles.Manage)]
    [CsrfProtected]
    [HttpPut("{id:guid}/permissions")]
    public async Task<IActionResult> SetPermissions(Guid id, SetRolePermissionsRequest request, CancellationToken ct)
    {
        await _mediator.Send(new SetRolePermissionsCommand(id, request.PermissionCodes), ct);
        return NoContent();
    }
}
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter RolesAdminTests`
Expected: PASS 4/4.

Run: `dotnet build && dotnet test`
Expected: 0 warning; toàn bộ xanh.

- [ ] **Step 6: Commit**

```bash
git add -A src tests/CleanArchCqrs.IntegrationTests
git commit -m "feat(roles): admin can list, create, rename roles and set role permissions"
```

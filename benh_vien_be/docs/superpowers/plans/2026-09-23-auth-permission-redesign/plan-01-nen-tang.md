# Giai đoạn 1 — Nền tảng: build sạch, hạ tầng dev, test tích hợp, lỗi, logging

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Solution build 0 warning; có docker-compose (Postgres, Redis, Seq); có project integration test chạy trên container thật; API có `/health`, Problem Details thống nhất và log JSON/Seq có che dữ liệu nhạy cảm.

**Spec:** [`spec.md`](spec.md) §1.3, §6.3–6.5 · **Ràng buộc chung:** [`plan.md`](plan.md#global-constraints)

Mọi lệnh chạy tại `benh_vien_be/` trừ khi ghi khác.

---

### Task 1.1: Build 0 warning, docker-compose, đánh dấu tài liệu cũ

**Files:**
- Modify: `src/CleanArchCqrs.Infrastructure/CleanArchCqrs.Infrastructure.csproj`
- Modify: `src/CleanArchCqrs.API/CleanArchCqrs.API.csproj`
- Modify: `src/CleanArchCqrs.Application/Auth/Models/LoginResult.cs:9`
- Create: `docker-compose.yml`
- Modify: `.sdd/Plan/luong-login.md`, `.sdd/Plan/07-fe-auth.md`, `.sdd/Plan/00-quyet-dinh-va-quy-uoc.md`

**Interfaces:**
- Consumes: —
- Produces: dịch vụ dev `postgres:5432` (user/pass `postgres`/`postgres`, DB `CleanArchCqrsDb`), `redis:6379`, `seq` UI tại `http://localhost:5341` (nhận log cũng ở `http://localhost:5341`).

- [ ] **Step 1: Ghi nhận 2 cảnh báo hiện tại**

Run: `dotnet build 2>&1 | grep -E "warning (MSB3277|CS8618)" | sort -u | head`
Expected: thấy `MSB3277` (xung đột `Microsoft.EntityFrameworkCore.Relational` 10.0.4 vs 10.0.12) và `CS8618` ở `LoginResult.cs`.

- [ ] **Step 2: Ghim `EntityFrameworkCore.Relational` 10.0.12 và thêm EF Design cho project khởi động**

```bash
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.EntityFrameworkCore.Relational --version 10.0.12
dotnet add src/CleanArchCqrs.API package Microsoft.EntityFrameworkCore.Design --version 10.0.12
```

`Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3` kéo `Relational 10.0.4`, còn các gói EF khác là 10.0.12. Ghim trực tiếp để hợp nhất phiên bản. `EF Design` ở API cần cho `dotnet ef migrations` ở giai đoạn 2 (project khởi động phải tham chiếu Design).

Sau khi add, mở `CleanArchCqrs.API.csproj` và sửa mục Design cho giống Infrastructure (không để lọt ra runtime):

```xml
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="10.0.12">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
```

- [ ] **Step 3: Sửa CS8618 tạm thời**

File này sẽ bị xoá ở Task 2.3; chỉ sửa đủ để hết cảnh báo. Trong `LoginResult.cs` đổi dòng 9:

```csharp
        public UserDto UserDto { get; set; } = default!;
```

- [ ] **Step 4: Build lại**

Run: `dotnet build 2>&1 | grep -cE " warning "`
Expected: `0`

- [ ] **Step 5: Tạo `docker-compose.yml`**

```yaml
services:
  postgres:
    image: postgres:17-alpine
    environment:
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: postgres
      POSTGRES_DB: CleanArchCqrsDb
    ports:
      - "5432:5432"
    volumes:
      - pgdata:/var/lib/postgresql/data

  redis:
    image: redis:7.4-alpine
    command: ["redis-server", "--appendonly", "yes"]
    ports:
      - "6379:6379"
    volumes:
      - redisdata:/data

  seq:
    image: datalust/seq:2024.3
    environment:
      ACCEPT_EULA: "Y"
      SEQ_FIRSTRUN_NOAUTHENTICATION: "true"
    ports:
      - "5341:80"
    volumes:
      - seqdata:/data

volumes:
  pgdata: {}
  redisdata: {}
  seqdata: {}
```

- [ ] **Step 6: Chạy thử hạ tầng**

Run: `docker compose up -d && docker compose ps`
Expected: 3 service `running`. Mở `http://localhost:5341` thấy giao diện Seq.

- [ ] **Step 7: Đánh dấu tài liệu cũ bị thay thế**

Chèn ngay dưới tiêu đề (dòng 1) của **`luong-login.md`** và **`07-fe-auth.md`**:

```markdown

> ⚠ **ĐÃ BỊ THAY THẾ (2026-09-23)** bởi `docs/superpowers/plans/2026-09-23-auth-permission-redesign/spec.md`
> và các file plan cùng thư mục. Giữ lại chỉ để tra cứu lịch sử — **không làm theo file này nữa.**
```

Trong **`00-quyet-dinh-va-quy-uoc.md`**, mục A, thay 3 dòng `Phát hành + validate token`, `Kiểu token`, `Phân quyền` và dòng `Audit đăng nhập` bằng:

```markdown
| Phát hành token | **CleanArchCqrs.API** (module IdentityAccess) | Cần DB user + transaction rotation |
| Xác thực mỗi request | **Gateway** validate JWT + phiên (Redis → BE); **API validate lại** chữ ký | Chặn sớm; BE không tin header từ client — xem spec 2026-09-23 |
| Kiểu token | JWT HS256 access **15 phút** (RAM trình duyệt) + refresh token rotation (cookie `__Host-rt`), phiên tuyệt đối 7 ngày | Theo Đặc tả kỹ thuật §4 |
| Phân quyền | **Permission-based**: nhiều role/user + quyền lẻ cấp thêm, `[HasPermission(...)]`, cache Redis không TTL | Spec 2026-09-23 |
| Audit truy cập/bảo mật | Bảng `AuditRecords` (login, refresh reuse, logout, 403, đổi quyền…) — **thay** `UserLoginHistory` | Theo Đặc tả kỹ thuật §3.3 |
```

- [ ] **Step 8: Commit**

```bash
git add src/CleanArchCqrs.Infrastructure/CleanArchCqrs.Infrastructure.csproj src/CleanArchCqrs.API/CleanArchCqrs.API.csproj src/CleanArchCqrs.Application/Auth/Models/LoginResult.cs docker-compose.yml .sdd/Plan/luong-login.md .sdd/Plan/07-fe-auth.md .sdd/Plan/00-quyet-dinh-va-quy-uoc.md
git commit -m "chore: fix build warnings, add docker-compose, mark old auth plans superseded"
```

---

### Task 1.2: Project integration test + Redis + `/health`

**Files:**
- Create: `tests/CleanArchCqrs.IntegrationTests/CleanArchCqrs.IntegrationTests.csproj`
- Create: `tests/CleanArchCqrs.IntegrationTests/Infrastructure/ContainersFixture.cs`
- Create: `tests/CleanArchCqrs.IntegrationTests/Infrastructure/IntegrationCollection.cs`
- Create: `tests/CleanArchCqrs.IntegrationTests/Infrastructure/TestConstants.cs`
- Create: `tests/CleanArchCqrs.IntegrationTests/Infrastructure/ApiFactory.cs`
- Create: `tests/CleanArchCqrs.IntegrationTests/Health/HealthEndpointTests.cs`
- Create: `src/CleanArchCqrs.Infrastructure/HealthChecks/DatabaseHealthCheck.cs`
- Create: `src/CleanArchCqrs.Infrastructure/HealthChecks/RedisHealthCheck.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs`, `src/CleanArchCqrs.API/appsettings.json`

**Interfaces:**
- Consumes: —
- Produces:
  - `ContainersFixture` (xUnit collection fixture): `string RedisConnectionString`, `Task<string> CreateDatabaseAsync()` (tạo DB rỗng mới, trả connection string).
  - `IntegrationCollection.Name = "integration"`.
  - `ApiFactory : WebApplicationFactory<CleanArchCqrs.API.Program>`: `static Task<ApiFactory> CreateAsync(ContainersFixture containers, IDictionary<string,string?>? overrides = null, Action<IServiceCollection>? configureServices = null)`, `string AdminEmail`, `string DatabaseConnectionString`, `HttpClient CreateHttpsClient()`.
  - `TestConstants`: `Origin`, `JwtIssuer`, `JwtAudience`, `JwtSigningKey`, `CsrfKey`, `InternalApiKey`, `AdminTempPassword`, `AdminPassword`.
  - DI: `IConnectionMultiplexer` singleton (`AbortOnConnectFail=false`, timeout 2s/1s); health check `database` (Unhealthy khi lỗi) và `redis` (**Degraded** khi lỗi); endpoint `GET /health`.
  - `Program.Main` thành `async Task Main`.

- [ ] **Step 1: Tạo project test**

`tests/CleanArchCqrs.IntegrationTests/CleanArchCqrs.IntegrationTests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CleanArchCqrs.API\CleanArchCqrs.API.csproj" />
    <ProjectReference Include="..\..\src\CleanArchCqrs.Gateway\CleanArchCqrs.Gateway.csproj" />
  </ItemGroup>

</Project>
```

```bash
dotnet add tests/CleanArchCqrs.IntegrationTests package Microsoft.NET.Test.Sdk --version 17.12.0
dotnet add tests/CleanArchCqrs.IntegrationTests package xunit --version 2.9.2
dotnet add tests/CleanArchCqrs.IntegrationTests package xunit.runner.visualstudio --version 2.8.2
dotnet add tests/CleanArchCqrs.IntegrationTests package Microsoft.AspNetCore.Mvc.Testing --version 10.0.12
dotnet add tests/CleanArchCqrs.IntegrationTests package Testcontainers.PostgreSql
dotnet add tests/CleanArchCqrs.IntegrationTests package Testcontainers.Redis
dotnet sln add tests/CleanArchCqrs.IntegrationTests/CleanArchCqrs.IntegrationTests.csproj --solution-folder tests
dotnet add src/CleanArchCqrs.Infrastructure package StackExchange.Redis
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.Extensions.Diagnostics.HealthChecks --version 10.0.12
```

- [ ] **Step 2: Fixture container**

`Infrastructure/ContainersFixture.cs`:

```csharp
using Npgsql;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Infrastructure;

/// Một Postgres + một Redis dùng chung cho cả collection. Mỗi ApiFactory tạo database riêng
/// để các test class không giẫm dữ liệu của nhau.
public sealed class ContainersFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:17-alpine").Build();
    private readonly RedisContainer _redis = new RedisBuilder().WithImage("redis:7.4-alpine").Build();

    public string RedisConnectionString => _redis.GetConnectionString();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"test_{Guid.NewGuid():N}";
        await using (var connection = new NpgsqlConnection(_postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()) { Database = name }.ConnectionString;
    }

    public Task InitializeAsync() => Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _redis.DisposeAsync();
    }
}
```

⚠ Nếu bản Testcontainers vừa cài báo `CS0618` (constructor không tham số bị obsolete), đổi sang dạng nhận image:
`new PostgreSqlBuilder("postgres:17-alpine").Build()` và `new RedisBuilder("redis:7.4-alpine").Build()`.

`Infrastructure/IntegrationCollection.cs`:

```csharp
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "integration";
}
```

`Infrastructure/TestConstants.cs`:

```csharp
namespace CleanArchCqrs.IntegrationTests.Infrastructure;

public static class TestConstants
{
    public const string Origin = "https://localhost";
    public const string JwtIssuer = "https://test.local";
    public const string JwtAudience = "hospital-management";
    public const string JwtSigningKey = "test-jwt-signing-key-0123456789-0123456789-0123456789";
    public const string CsrfKey = "test-csrf-key-0123456789-0123456789-0123456789-abcdef";
    public const string InternalApiKey = "test-internal-key-0123456789";
    public const string AdminTempPassword = "Admin-Temp-Pass-01";
    public const string AdminPassword = "Admin-Real-Pass-02";
}
```

`Infrastructure/ApiFactory.cs` (khai báo sẵn mọi khoá cấu hình các giai đoạn sau cần — khoá chưa dùng không gây hại):

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.IntegrationTests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<CleanArchCqrs.API.Program>
{
    private readonly Dictionary<string, string?> _settings;
    private readonly Action<IServiceCollection>? _configureServices;

    public string AdminEmail { get; }
    public string DatabaseConnectionString { get; }

    private ApiFactory(string databaseConnectionString, string redisConnectionString,
        IDictionary<string, string?>? overrides, Action<IServiceCollection>? configureServices)
    {
        DatabaseConnectionString = databaseConnectionString;
        AdminEmail = $"admin-{Guid.NewGuid():N}@test.local";
        _configureServices = configureServices;
        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = databaseConnectionString,
            ["ConnectionStrings:Redis"] = redisConnectionString,
            ["Database:MigrateOnStartup"] = "true",
            ["Jwt:Issuer"] = TestConstants.JwtIssuer,
            ["Jwt:Audience"] = TestConstants.JwtAudience,
            ["Jwt:SigningKey"] = TestConstants.JwtSigningKey,
            ["Auth:CsrfKey"] = TestConstants.CsrfKey,
            ["Auth:InternalApiKey"] = TestConstants.InternalApiKey,
            ["Auth:AllowedOrigins:0"] = TestConstants.Origin,
            ["Seed:AdminEmail"] = AdminEmail,
            ["Seed:AdminPassword"] = TestConstants.AdminTempPassword,
            ["Seed:AdminFullName"] = "Test Admin",
        };
        if (overrides is not null)
            foreach (var (key, value) in overrides)
                _settings[key] = value;
    }

    public static async Task<ApiFactory> CreateAsync(ContainersFixture containers,
        IDictionary<string, string?>? overrides = null, Action<IServiceCollection>? configureServices = null)
        => new(await containers.CreateDatabaseAsync(), containers.RedisConnectionString, overrides, configureServices);

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
        if (_configureServices is not null)
            builder.ConfigureTestServices(_configureServices);
    }
}
```

- [ ] **Step 3: Viết test health (đỏ)**

`Health/HealthEndpointTests.cs`:

```csharp
using System.Net;
using CleanArchCqrs.IntegrationTests.Infrastructure;
using Xunit;

namespace CleanArchCqrs.IntegrationTests.Health;

[Collection(IntegrationCollection.Name)]
public class HealthEndpointTests
{
    private readonly ContainersFixture _containers;

    public HealthEndpointTests(ContainersFixture containers) => _containers = containers;

    [Fact]
    public async Task Health_AllDependenciesUp_ReturnsHealthy()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers);

        var response = await factory.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Health_RedisDown_ReturnsDegradedButStillOk()
    {
        await using var factory = await ApiFactory.CreateAsync(_containers,
            new Dictionary<string, string?> { ["ConnectionStrings:Redis"] = "127.0.0.1:1" });

        var response = await factory.CreateHttpsClient().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Degraded", await response.Content.ReadAsStringAsync());
    }
}
```

- [ ] **Step 4: Chạy test, xác nhận đỏ** (Docker Desktop phải đang chạy)

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter HealthEndpointTests`
Expected: FAIL — `/health` trả 404.

- [ ] **Step 5: Health check + Redis trong Infrastructure**

`HealthChecks/DatabaseHealthCheck.cs`:

```csharp
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CleanArchCqrs.Infrastructure.HealthChecks;

public sealed class DatabaseHealthCheck : IHealthCheck
{
    private readonly AppDbContext _db;

    public DatabaseHealthCheck(AppDbContext db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
        => await _db.Database.CanConnectAsync(ct)
            ? HealthCheckResult.Healthy()
            : new HealthCheckResult(context.Registration.FailureStatus, "Database unreachable");
}
```

`HealthChecks/RedisHealthCheck.cs`:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace CleanArchCqrs.Infrastructure.HealthChecks;

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

Trong `InfrastructureServiceExtensions.AddInfrastructureServices`, thêm trước `return services;` (và `using` tương ứng: `CleanArchCqrs.Infrastructure.HealthChecks`, `Microsoft.Extensions.Diagnostics.HealthChecks`, `StackExchange.Redis`):

```csharp
        services.AddSingleton<IConnectionMultiplexer>(_ =>
        {
            var options = ConfigurationOptions.Parse(configuration.GetConnectionString("Redis") ?? "localhost:6379");
            options.AbortOnConnectFail = false;   // Redis sập lúc khởi động không được làm sập app
            options.ConnectTimeout = 2000;
            options.SyncTimeout = 1000;
            options.AsyncTimeout = 1000;
            return ConnectionMultiplexer.Connect(options);
        });

        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<RedisHealthCheck>("redis", failureStatus: HealthStatus.Degraded);
```

- [ ] **Step 6: `Program.cs` và cấu hình**

Trong `Program.cs`:
- `public static void Main(string[] args)` → `public static async Task Main(string[] args)`
- thêm `app.MapHealthChecks("/health");` ngay sau `app.MapControllers();`
- `app.Run();` → `await app.RunAsync();`

Trong `appsettings.json`, mục `ConnectionStrings` thêm:

```json
    "Redis": "localhost:6379"
```

- [ ] **Step 7: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.IntegrationTests --filter HealthEndpointTests`
Expected: PASS 2/2.

Run: `dotnet test tests/CleanArchCqrs.UnitTests`
Expected: PASS (không hỏng test cũ).

- [ ] **Step 8: Commit**

```bash
git add tests/CleanArchCqrs.IntegrationTests src/CleanArchCqrs.Infrastructure src/CleanArchCqrs.API benh_vien_be.sln
git commit -m "test: add integration test project with Testcontainers; feat(api): add Redis connection and /health"
```

---

### Task 1.3: Problem Details thống nhất

**Files:**
- Create: `src/CleanArchCqrs.Application/Common/Exceptions/ErrorCodes.cs`
- Create: `src/CleanArchCqrs.Application/Common/Exceptions/UnauthorizedException.cs`
- Create: `src/CleanArchCqrs.Application/Common/Exceptions/ForbiddenException.cs`
- Create: `src/CleanArchCqrs.Application/Common/Exceptions/ConflictException.cs`
- Create: `src/CleanArchCqrs.Application/Common/Exceptions/TooManyRequestsException.cs`
- Modify: `src/CleanArchCqrs.Application/Common/Exceptions/ValidationException.cs`
- Create: `src/CleanArchCqrs.API/Errors/ProblemResponseWriter.cs`
- Create: `src/CleanArchCqrs.API/Errors/GlobalExceptionHandler.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs`
- Test: `tests/CleanArchCqrs.UnitTests/API/Errors/GlobalExceptionHandlerTests.cs`

**Interfaces:**
- Consumes: `ValidationException.Errors : IDictionary<string,string[]>` (đã có), `NotFoundException`, `BusinessRuleViolationException` (Domain, đã có).
- Produces:
  - `ErrorCodes` (hằng `ValidationFailed="validation_failed"`, `Unauthenticated`, `Forbidden`, `PasswordChangeRequired`, `CsrfFailed`, `NotFound`, `EmailTaken`, `LastAdmin`, `SelfActionForbidden`, `Conflict`, `RateLimited`, `DependencyUnavailable`, `InternalError`).
  - `UnauthorizedException(string message)` → 401; `ForbiddenException(string code, string message)` → 403 (`Code`); `ConflictException(string code, string message)` → 409 (`Code`); `TooManyRequestsException(TimeSpan retryAfter, string message)` → 429 (`RetryAfter`); `ValidationException(string propertyName, string error)` (ctor mới).
  - `ProblemResponseWriter.BuildBody(HttpContext, int status, string code, string title, IDictionary<string,string[]>? errors = null) : Dictionary<string, object?>`, `WriteAsync(HttpContext, int status, string code, string title, IDictionary<string,string[]>? errors = null, CancellationToken ct = default)`, `ToResult(HttpContext, int status, string code, string title, IDictionary<string,string[]>? errors = null) : ObjectResult`.
  - `[ApiController]` model-binding lỗi cũng trả format này (400 `validation_failed`).

- [ ] **Step 1: Viết test (đỏ)**

`tests/CleanArchCqrs.UnitTests/API/Errors/GlobalExceptionHandlerTests.cs`:

```csharp
using System.Text.Json;
using CleanArchCqrs.API.Errors;
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Domain.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Errors;

public class GlobalExceptionHandlerTests
{
    private static async Task<(HttpContext Context, JsonElement Body)> HandleAsync(Exception exception)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "corr-123";
        context.Response.Body = new MemoryStream();

        var handled = await new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance)
            .TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return (context, document.RootElement.Clone());
    }

    [Fact]
    public async Task Validation_Returns400WithCamelCaseErrors()
    {
        var (context, body) = await HandleAsync(new ValidationException("CurrentPassword", "Sai"));

        Assert.Equal(400, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);
        Assert.Equal("validation_failed", body.GetProperty("code").GetString());
        Assert.Equal("corr-123", body.GetProperty("traceId").GetString());
        Assert.Equal("Sai", body.GetProperty("errors").GetProperty("currentPassword")[0].GetString());
    }

    [Fact]
    public async Task Unauthorized_Returns401WithMessageAsTitle()
    {
        var (context, body) = await HandleAsync(new UnauthorizedException("Email hoặc mật khẩu không đúng."));

        Assert.Equal(401, context.Response.StatusCode);
        Assert.Equal("unauthenticated", body.GetProperty("code").GetString());
        Assert.Equal("Email hoặc mật khẩu không đúng.", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Forbidden_UsesExceptionCode()
    {
        var (context, body) = await HandleAsync(new ForbiddenException("csrf_failed", "CSRF"));

        Assert.Equal(403, context.Response.StatusCode);
        Assert.Equal("csrf_failed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task NotFound_Returns404WithGenericTitle()
    {
        var (context, body) = await HandleAsync(new NotFoundException("User with id 'x' was not found."));

        Assert.Equal(404, context.Response.StatusCode);
        Assert.Equal("not_found", body.GetProperty("code").GetString());
        Assert.DoesNotContain("x", body.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Conflict_UsesExceptionCode()
    {
        var (context, body) = await HandleAsync(new ConflictException("last_admin", "Còn 1 admin"));

        Assert.Equal(409, context.Response.StatusCode);
        Assert.Equal("last_admin", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task TooManyRequests_SetsRetryAfterSeconds()
    {
        var (context, body) = await HandleAsync(new TooManyRequestsException(TimeSpan.FromSeconds(89.2), "Chậm lại"));

        Assert.Equal(429, context.Response.StatusCode);
        Assert.Equal("90", context.Response.Headers.RetryAfter.ToString());
        Assert.Equal("rate_limited", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Unknown_Returns500WithoutLeakingMessage()
    {
        var (context, body) = await HandleAsync(new InvalidOperationException("SELECT * FROM secret"));

        Assert.Equal(500, context.Response.StatusCode);
        Assert.Equal("internal_error", body.GetProperty("code").GetString());
        Assert.DoesNotContain("SELECT", body.ToString());
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter GlobalExceptionHandlerTests`
Expected: FAIL biên dịch — `GlobalExceptionHandler`, `UnauthorizedException`… chưa tồn tại.

- [ ] **Step 3: Exception + mã lỗi ở Application**

`Common/Exceptions/ErrorCodes.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Exceptions;

public static class ErrorCodes
{
    public const string ValidationFailed = "validation_failed";
    public const string Unauthenticated = "unauthenticated";
    public const string Forbidden = "forbidden";
    public const string PasswordChangeRequired = "password_change_required";
    public const string CsrfFailed = "csrf_failed";
    public const string NotFound = "not_found";
    public const string EmailTaken = "email_taken";
    public const string LastAdmin = "last_admin";
    public const string SelfActionForbidden = "self_action_forbidden";
    public const string Conflict = "conflict";
    public const string RateLimited = "rate_limited";
    public const string DependencyUnavailable = "dependency_unavailable";
    public const string InternalError = "internal_error";
}
```

`Common/Exceptions/UnauthorizedException.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}
```

`Common/Exceptions/ForbiddenException.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class ForbiddenException : Exception
{
    public string Code { get; }

    public ForbiddenException(string code, string message) : base(message) => Code = code;
}
```

`Common/Exceptions/ConflictException.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class ConflictException : Exception
{
    public string Code { get; }

    public ConflictException(string code, string message) : base(message) => Code = code;
}
```

`Common/Exceptions/TooManyRequestsException.cs`:

```csharp
namespace CleanArchCqrs.Application.Common.Exceptions;

public sealed class TooManyRequestsException : Exception
{
    public TimeSpan RetryAfter { get; }

    public TooManyRequestsException(TimeSpan retryAfter, string message) : base(message) => RetryAfter = retryAfter;
}
```

Trong `ValidationException.cs` thêm constructor sau constructor nhận `failures`:

```csharp
    public ValidationException(string propertyName, string error)
        : this()
    {
        Errors = new Dictionary<string, string[]> { [propertyName] = [error] };
    }
```

- [ ] **Step 4: Writer + handler ở API**

`Errors/ProblemResponseWriter.cs`:

```csharp
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace CleanArchCqrs.API.Errors;

/// Một nơi duy nhất dựng body Problem Details (RFC 9457) cho mọi lỗi của API.
public static class ProblemResponseWriter
{
    private const string ContentType = "application/problem+json";

    public static Dictionary<string, object?> BuildBody(HttpContext context, int status, string code, string title,
        IDictionary<string, string[]>? errors = null)
    {
        var correlationId = context.Request.Headers["X-Correlation-Id"].ToString();
        var body = new Dictionary<string, object?>
        {
            ["type"] = "about:blank",
            ["title"] = title,
            ["status"] = status,
            ["code"] = code,
            ["traceId"] = string.IsNullOrEmpty(correlationId) ? context.TraceIdentifier : correlationId,
        };
        if (errors is not null)
            body["errors"] = errors.ToDictionary(e => JsonNamingPolicy.CamelCase.ConvertName(e.Key), e => e.Value);
        return body;
    }

    public static async Task WriteAsync(HttpContext context, int status, string code, string title,
        IDictionary<string, string[]>? errors = null, CancellationToken ct = default)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = ContentType;
        await context.Response.WriteAsync(JsonSerializer.Serialize(BuildBody(context, status, code, title, errors)), ct);
    }

    public static ObjectResult ToResult(HttpContext context, int status, string code, string title,
        IDictionary<string, string[]>? errors = null)
        => new(BuildBody(context, status, code, title, errors)) { StatusCode = status, ContentTypes = { ContentType } };
}
```

`Errors/GlobalExceptionHandler.cs`:

```csharp
using CleanArchCqrs.Application.Common.Exceptions;
using CleanArchCqrs.Domain.Exceptions;
using Microsoft.AspNetCore.Diagnostics;

namespace CleanArchCqrs.API.Errors;

public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) => _logger = logger;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        switch (exception)
        {
            case ValidationException validation:
                await ProblemResponseWriter.WriteAsync(context, 400, ErrorCodes.ValidationFailed,
                    "Dữ liệu không hợp lệ.", validation.Errors, ct);
                break;
            case UnauthorizedException unauthorized:
                await ProblemResponseWriter.WriteAsync(context, 401, ErrorCodes.Unauthenticated, unauthorized.Message, ct: ct);
                break;
            case ForbiddenException forbidden:
                await ProblemResponseWriter.WriteAsync(context, 403, forbidden.Code, forbidden.Message, ct: ct);
                break;
            case NotFoundException:
                await ProblemResponseWriter.WriteAsync(context, 404, ErrorCodes.NotFound, "Không tìm thấy tài nguyên.", ct: ct);
                break;
            case ConflictException conflict:
                await ProblemResponseWriter.WriteAsync(context, 409, conflict.Code, conflict.Message, ct: ct);
                break;
            case BusinessRuleViolationException rule:
                await ProblemResponseWriter.WriteAsync(context, 409, ErrorCodes.Conflict, rule.Message, ct: ct);
                break;
            case TooManyRequestsException tooMany:
                context.Response.Headers.RetryAfter = ((int)Math.Ceiling(tooMany.RetryAfter.TotalSeconds)).ToString();
                await ProblemResponseWriter.WriteAsync(context, 429, ErrorCodes.RateLimited, tooMany.Message, ct: ct);
                break;
            default:
                _logger.LogError(exception, "Unhandled exception");
                await ProblemResponseWriter.WriteAsync(context, 500, ErrorCodes.InternalError, "Đã có lỗi xảy ra.", ct: ct);
                break;
        }

        return true;
    }
}
```

- [ ] **Step 5: Đăng ký trong `Program.cs`**

Thêm `using CleanArchCqrs.API.Errors;`, `using CleanArchCqrs.Application.Common.Exceptions;`, `using Microsoft.AspNetCore.Mvc;`.

Sau `builder.Services.AddControllers()...;` thêm:

```csharp
        builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
        builder.Services.AddProblemDetails();
        builder.Services.Configure<ApiBehaviorOptions>(options =>
            options.InvalidModelStateResponseFactory = context =>
                ProblemResponseWriter.ToResult(context.HttpContext, 400, ErrorCodes.ValidationFailed, "Dữ liệu không hợp lệ.",
                    context.ModelState.Where(e => e.Value?.Errors.Count > 0)
                        .ToDictionary(e => e.Key, e => e.Value!.Errors.Select(x => x.ErrorMessage).ToArray())));
```

Trong pipeline, ngay sau `app.UseMiddleware<CorrelationIdMiddleware>();` thêm:

```csharp
        app.UseExceptionHandler();
```

- [ ] **Step 6: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter GlobalExceptionHandlerTests`
Expected: PASS 7/7. Chạy thêm `dotnet build` → 0 warning.

- [ ] **Step 7: Commit**

```bash
git add src/CleanArchCqrs.Application/Common/Exceptions src/CleanArchCqrs.API tests/CleanArchCqrs.UnitTests/API
git commit -m "feat(api): add RFC 9457 problem details with stable error codes"
```

---

### Task 1.4: Logging có cấu trúc (JSON + Seq) và che dữ liệu nhạy cảm

**Files:**
- Create: `src/CleanArchCqrs.API/Logging/SensitiveDataDestructuringPolicy.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs`, `src/CleanArchCqrs.API/appsettings.json`
- Modify: `src/CleanArchCqrs.Gateway/appsettings.json`
- Modify: `src/CleanArchCqrs.API/CleanArchCqrs.API.csproj`, `src/CleanArchCqrs.Gateway/CleanArchCqrs.Gateway.csproj`
- Test: `tests/CleanArchCqrs.UnitTests/API/Logging/SensitiveDataDestructuringPolicyTests.cs`

**Interfaces:**
- Consumes: —
- Produces: `SensitiveDataDestructuringPolicy : Serilog.Core.IDestructuringPolicy` — object có property nhạy cảm (danh sách ở Global Constraints) khi log bằng `{@...}` sẽ hiện `***` cho property đó. Mọi dòng log có property `Service` (`api`/`gateway`).

- [ ] **Step 1: Viết test (đỏ)**

`tests/CleanArchCqrs.UnitTests/API/Logging/SensitiveDataDestructuringPolicyTests.cs`:

```csharp
using CleanArchCqrs.API.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Logging;

sealed class CollectingSink : ILogEventSink
{
    public List<LogEvent> Events { get; } = new();
    public void Emit(LogEvent logEvent) => Events.Add(logEvent);
}

public class SensitiveDataDestructuringPolicyTests
{
    private static string Render(object value)
    {
        var sink = new CollectingSink();
        using (var logger = new LoggerConfiguration()
                   .Destructure.With<SensitiveDataDestructuringPolicy>()
                   .WriteTo.Sink(sink)
                   .CreateLogger())
        {
            logger.Information("Value {@Value}", value);
        }
        return sink.Events.Single().Properties["Value"].ToString();
    }

    [Fact]
    public void MasksPasswordAndTokens_KeepsOtherFields()
    {
        var rendered = Render(new { Email = "a@b.vn", Password = "secret-1", RefreshToken = "rt-value", AccessToken = "at-value" });

        Assert.Contains("a@b.vn", rendered);
        Assert.DoesNotContain("secret-1", rendered);
        Assert.DoesNotContain("rt-value", rendered);
        Assert.DoesNotContain("at-value", rendered);
        Assert.Contains("***", rendered);
    }

    [Fact]
    public void MatchesPropertyNamesCaseInsensitively()
    {
        var rendered = Render(new { newPassword = "n-secret", currentPassword = "c-secret" });

        Assert.DoesNotContain("n-secret", rendered);
        Assert.DoesNotContain("c-secret", rendered);
    }

    [Fact]
    public void ObjectsWithoutSensitiveFields_UseDefaultDestructuring()
    {
        var rendered = Render(new { PageNumber = 2, SearchTerm = "abc" });

        Assert.Contains("2", rendered);
        Assert.Contains("abc", rendered);
        Assert.DoesNotContain("***", rendered);
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter SensitiveDataDestructuringPolicyTests`
Expected: FAIL biên dịch — type chưa có.

- [ ] **Step 3: Viết policy**

`Logging/SensitiveDataDestructuringPolicy.cs`:

```csharp
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Serilog.Core;
using Serilog.Events;

namespace CleanArchCqrs.API.Logging;

/// Khi một object được log bằng {@...}, thay giá trị các property nhạy cảm bằng "***".
public sealed class SensitiveDataDestructuringPolicy : IDestructuringPolicy
{
    private const string Mask = "***";

    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Password", "CurrentPassword", "NewPassword", "TemporaryPassword",
        "AccessToken", "RefreshToken", "PasswordHash", "TokenHash",
    };

    public bool TryDestructure(object value, ILogEventPropertyValueFactory propertyValueFactory,
        [NotNullWhen(true)] out LogEventPropertyValue? result)
    {
        result = null;
        var type = value.GetType();
        if (value is string or IEnumerable || type.IsPrimitive || type.IsEnum)
            return false;

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList();
        if (!properties.Any(p => SensitiveNames.Contains(p.Name)))
            return false;

        result = new StructureValue(
            properties.Select(p => new LogEventProperty(p.Name,
                SensitiveNames.Contains(p.Name)
                    ? new ScalarValue(Mask)
                    : propertyValueFactory.CreatePropertyValue(p.GetValue(value), destructureObjects: true))),
            type.Name);
        return true;
    }
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter SensitiveDataDestructuringPolicyTests`
Expected: PASS 3/3.

- [ ] **Step 5: Gắn policy + sink Seq/JSON**

```bash
dotnet add src/CleanArchCqrs.API package Serilog.Sinks.Seq
dotnet add src/CleanArchCqrs.Gateway package Serilog.Sinks.Seq
```

Trong `Program.cs` (API), lambda `UseSerilog` thành:

```csharp
        builder.Host.UseSerilog((context, services, configuration) => configuration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Destructure.With<SensitiveDataDestructuringPolicy>());
```

(thêm `using CleanArchCqrs.API.Logging;`).

Trong `src/CleanArchCqrs.API/appsettings.json`, thay mảng `WriteTo` và thêm `Properties`:

```json
    "WriteTo": [
      { "Name": "Console", "Args": { "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{CorrelationId}] {Message:lj}{NewLine}{Exception}" } },
      { "Name": "File", "Args": { "path": "logs/api-.json", "rollingInterval": "Day", "retainedFileCountLimit": 14, "formatter": "Serilog.Formatting.Compact.CompactJsonFormatter, Serilog.Formatting.Compact" } },
      { "Name": "Seq", "Args": { "serverUrl": "http://localhost:5341" } }
    ],
    "Enrich": [ "FromLogContext" ],
    "Properties": { "Service": "api" }
```

Trong `src/CleanArchCqrs.Gateway/appsettings.json` làm tương tự, chỉ khác `path` = `logs/gateway-.json` và `"Properties": { "Service": "gateway" }`.

- [ ] **Step 6: Kiểm tra tay**

Run: `docker compose up -d seq && dotnet run --project src/CleanArchCqrs.API` rồi mở `http://localhost:5289/health` vài lần, dừng app.
Expected: Seq (`http://localhost:5341`) có sự kiện `HTTP GET /health responded 200` với property `Service = api` và `CorrelationId`; thư mục `src/CleanArchCqrs.API/logs/` có file `api-YYYYMMDD.json`, mỗi dòng một object JSON.

- [ ] **Step 7: Commit**

```bash
git add src/CleanArchCqrs.API src/CleanArchCqrs.Gateway tests/CleanArchCqrs.UnitTests/API/Logging
git commit -m "feat(logging): add Seq and compact JSON sinks, mask sensitive properties"
```

# Audit, Logging & Login Phần 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the audit trail (`AuditLog` via EF Core interceptor), Serilog-based logging with cross-service correlation IDs, and the Infrastructure layer of the login flow (Phần 5 of `luong-login.md`) real, tested code.

**Architecture:** Domain gains audit contracts (`IAuditable`/`AuditAction`/`AuditLog`) and `UserLoginHistory` with zero external dependencies. Infrastructure implements persistence (`AppDbContext`, EF configurations, repositories, `AuditSaveChangesInterceptor`, JWT/password security) and wires it all through DI. API and Gateway each get Serilog request logging and a correlation-ID middleware so one request can be traced across both processes' log files.

**Tech Stack:** .NET 10 / ASP.NET Core, EF Core 10 (Npgsql + InMemory for tests), Serilog.AspNetCore, Microsoft.IdentityModel.JsonWebTokens, xUnit.

**Spec:**
- `.sdd/Plan/luong-audit-logging.md` (Phần A–E — audit interceptor, Serilog, correlation ID)
- `.sdd/Plan/luong-login.md` Phần 5 only (5.0.4–5.0.5, 5.1–5.5 — Infrastructure for login)
- `.sdd/Plan/00-quyet-dinh-va-quy-uoc.md` (project-wide conventions referenced below)

## Global Constraints

- Domain project (`CleanArchCqrs.Domain`) must have **0** `<PackageReference>` and **0** `<ProjectReference>` at all times — verify with `dotnet build src/CleanArchCqrs.Domain` after every Domain-touching task.
- File-scoped namespaces, one file = one type, filename = type name.
- Async methods take `CancellationToken ct = default` and end in `Async`.
- Primary keys are `Guid.CreateVersion7()`, generated client-side.
- Timestamps are `DateTimeOffset`, stored UTC.
- Gateway project (`CleanArchCqrs.Gateway`) has zero `<ProjectReference>` — it never references Domain/Application/Infrastructure/API. Its correlation-ID middleware is therefore a standalone copy, not a shared class.
- `AuditLog` is insert-only and must **never** implement `IAuditable` (would self-audit forever).
- Any code that serializes entity property changes must exclude the `PasswordHash` property by name.
- Database is PostgreSQL via Npgsql; `UseSqlServer`/the SQL-Server connection-string syntax must not reappear.
- DI registration in `AddInfrastructureServices` uses the `(sp, opt) => ...` lambda form (needed to resolve `AuditSaveChangesInterceptor` from the container).
- Test project targets `net10.0`, uses xUnit, and the EF Core `InMemory` provider for repository/interceptor/configuration tests (no real Postgres in unit tests).

---

## File Structure

**New test project:**
- `tests/CleanArchCqrs.UnitTests/CleanArchCqrs.UnitTests.csproj`

**Domain (`src/CleanArchCqrs.Domain/`):**
- Create `Common/Auditing/IAuditable.cs`, `Common/Auditing/AuditAction.cs`, `Common/Auditing/AuditLog.cs`
- Delete `Audit/AuditLog.cs` (empty stub, wrong location)
- Modify `Identity/User.cs` (implement `IAuditable`)
- Create `Identity/UserLoginHistory.cs`, `Identity/IUserLoginHistoryRepository.cs`

**Infrastructure (`src/CleanArchCqrs.Infrastructure/`):**
- Delete `Audit/AuditSaveChangesInterceptor.cs` (empty stub, wrong location)
- Create `Persistence/Interceptors/AuditSaveChangesInterceptor.cs`
- Modify `Persistence/AppDbContext.cs`
- Create `Persistence/Configurations/UserConfiguration.cs`, `Persistence/Configurations/UserLoginHistoryConfiguration.cs`, `Persistence/Configurations/AuditLogConfiguration.cs`
- Create `Repositories/UserRepository.cs`, `Repositories/UserLoginHistoryRepository.cs`
- Create `Security/PasswordHasher.cs`, `Security/JwtOptions.cs`, `Security/JwtTokenService.cs`
- Modify `DependencyInjection/InfrastructureServiceExtensions.cs`, `CleanArchCqrs.Infrastructure.csproj`

**API (`src/CleanArchCqrs.API/`):**
- Modify `CleanArchCqrs.API.csproj`, `appsettings.json`, `appsettings.Development.json`, `Program.cs`
- Create `Middleware/CorrelationIdMiddleware.cs`

**Gateway (`src/CleanArchCqrs.Gateway/`):**
- Modify `CleanArchCqrs.Gateway.csproj`, `appsettings.json`, `appsettings.Development.json` (create it — doesn't exist yet), `Program.cs`
- Create `Middleware/CorrelationIdMiddleware.cs`

---

### Task 1: Test project scaffold

**Files:**
- Create: `tests/CleanArchCqrs.UnitTests/CleanArchCqrs.UnitTests.csproj`
- Create: `tests/CleanArchCqrs.UnitTests/SanityTests.cs`
- Modify: `benh_vien_be.sln`

**Interfaces:**
- Consumes: nothing (first task)
- Produces: an xUnit test project referencing `Domain`, `Application`, `Infrastructure`, `API`, `Gateway`, with an ASP.NET Core `FrameworkReference` (needed later for `DefaultHttpContext` in middleware tests). All later tasks add test files under `tests/CleanArchCqrs.UnitTests/`.

- [ ] **Step 1: Create the test project file**

```xml
<!-- tests/CleanArchCqrs.UnitTests/CleanArchCqrs.UnitTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\CleanArchCqrs.Domain\CleanArchCqrs.Domain.csproj" />
    <ProjectReference Include="..\..\src\CleanArchCqrs.Application\CleanArchCqrs.Application.csproj" />
    <ProjectReference Include="..\..\src\CleanArchCqrs.Infrastructure\CleanArchCqrs.Infrastructure.csproj" />
    <ProjectReference Include="..\..\src\CleanArchCqrs.API\CleanArchCqrs.API.csproj" />
    <ProjectReference Include="..\..\src\CleanArchCqrs.Gateway\CleanArchCqrs.Gateway.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Write a trivial sanity test**

```csharp
// tests/CleanArchCqrs.UnitTests/SanityTests.cs
using Xunit;

namespace CleanArchCqrs.UnitTests;

public class SanityTests
{
    [Fact]
    public void Xunit_IsWired() => Assert.True(true);
}
```

- [ ] **Step 3: Add the project to the solution and run it**

```bash
dotnet sln benh_vien_be.sln add tests/CleanArchCqrs.UnitTests/CleanArchCqrs.UnitTests.csproj
dotnet test tests/CleanArchCqrs.UnitTests/CleanArchCqrs.UnitTests.csproj
```

Expected: `Passed! - Failed: 0, Passed: 1, Skipped: 0`.

- [ ] **Step 4: Commit**

```bash
git add tests/CleanArchCqrs.UnitTests benh_vien_be.sln
git commit -m "test: scaffold xUnit test project"
```

---

### Task 2: Domain — Audit contracts (`IAuditable`, `AuditAction`, `AuditLog`)

**Files:**
- Delete: `src/CleanArchCqrs.Domain/Audit/AuditLog.cs` (empty stub, wrong folder)
- Create: `src/CleanArchCqrs.Domain/Common/Auditing/IAuditable.cs`
- Create: `src/CleanArchCqrs.Domain/Common/Auditing/AuditAction.cs`
- Create: `src/CleanArchCqrs.Domain/Common/Auditing/AuditLog.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Domain/Common/Auditing/AuditLogTests.cs`

**Interfaces:**
- Consumes: `CleanArchCqrs.Domain.Common.Entity<TId>` (already exists)
- Produces: `IAuditable` (marker interface), `AuditAction` enum (`Created`/`Updated`/`Deleted`), `AuditLog.Create(string entityName, string entityId, AuditAction action, Guid? changedByUserId, string changesJson) : AuditLog` with public getters `Id`, `EntityName`, `EntityId`, `Action`, `ChangedByUserId`, `ChangedAt`, `Changes` — consumed by Task 3 (`User : IAuditable`), Task 5 (interceptor calls `AuditLog.Create`), Task 6 (`AppDbContext.AuditLogs` and its EF configuration).

- [ ] **Step 1: Delete the empty stray file**

```bash
git rm src/CleanArchCqrs.Domain/Audit/AuditLog.cs
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/CleanArchCqrs.UnitTests/Domain/Common/Auditing/AuditLogTests.cs
using CleanArchCqrs.Domain.Common.Auditing;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Common.Auditing;

public class AuditLogTests
{
    [Fact]
    public void Create_SetsAllFieldsAndGeneratesId()
    {
        var changedBy = Guid.NewGuid();

        var log = AuditLog.Create("User", "abc-123", AuditAction.Updated, changedBy,
            "{\"FullName\":{\"Old\":\"A\",\"New\":\"B\"}}");

        Assert.NotEqual(Guid.Empty, log.Id);
        Assert.Equal("User", log.EntityName);
        Assert.Equal("abc-123", log.EntityId);
        Assert.Equal(AuditAction.Updated, log.Action);
        Assert.Equal(changedBy, log.ChangedByUserId);
        Assert.Equal("{\"FullName\":{\"Old\":\"A\",\"New\":\"B\"}}", log.Changes);
    }

    [Fact]
    public void Create_WithNullChangedBy_AllowsSystemActions()
    {
        var log = AuditLog.Create("User", "abc-123", AuditAction.Created, null, "{}");

        Assert.Null(log.ChangedByUserId);
    }

    [Fact]
    public void AuditLog_DoesNotImplementIAuditable()
    {
        Assert.False(typeof(IAuditable).IsAssignableFrom(typeof(AuditLog)));
    }
}
```

- [ ] **Step 3: Run tests to verify they fail (types don't exist yet)**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter AuditLogTests`
Expected: build error — `AuditLog`/`AuditAction` not found.

- [ ] **Step 4: Create `IAuditable.cs`**

```csharp
namespace CleanArchCqrs.Domain.Common.Auditing;

/// Entity implement interface này sẽ được AuditSaveChangesInterceptor tự ghi vào AuditLog mỗi khi Add/Modify/Delete.
public interface IAuditable
{
}
```

- [ ] **Step 5: Create `AuditAction.cs`**

```csharp
namespace CleanArchCqrs.Domain.Common.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted
}
```

- [ ] **Step 6: Create `AuditLog.cs`**

```csharp
namespace CleanArchCqrs.Domain.Common.Auditing;

public sealed class AuditLog : Entity<Guid>
{
    public string EntityName { get; private set; } = default!;
    public string EntityId { get; private set; } = default!;
    public AuditAction Action { get; private set; }
    public Guid? ChangedByUserId { get; private set; }
    public DateTimeOffset ChangedAt { get; private set; }
    public string Changes { get; private set; } = default!;

    private AuditLog() { }

    public static AuditLog Create(string entityName, string entityId, AuditAction action,
        Guid? changedByUserId, string changesJson)
    {
        return new AuditLog
        {
            Id = Guid.CreateVersion7(),
            EntityName = entityName,
            EntityId = entityId,
            Action = action,
            ChangedByUserId = changedByUserId,
            ChangedAt = DateTimeOffset.UtcNow,
            Changes = changesJson
        };
    }
}
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter AuditLogTests`
Expected: 3 passed.

- [ ] **Step 8: Verify Domain still has zero packages/references**

Run: `dotnet build src/CleanArchCqrs.Domain`
Expected: 0 error, 0 warning.

- [ ] **Step 9: Commit**

```bash
git add src/CleanArchCqrs.Domain/Common/Auditing src/CleanArchCqrs.Domain/Audit tests/CleanArchCqrs.UnitTests/Domain/Common/Auditing
git commit -m "feat(domain): add IAuditable, AuditAction, AuditLog contracts"
```

---

### Task 3: Domain — Apply `IAuditable` to `User`

**Files:**
- Modify: `src/CleanArchCqrs.Domain/Identity/User.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Domain/Identity/UserAuditableTests.cs`

**Interfaces:**
- Consumes: `IAuditable` (Task 2)
- Produces: `User` now satisfies `IAuditable`, so Task 5's interceptor picks it up automatically once it's tracked by EF.

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CleanArchCqrs.UnitTests/Domain/Identity/UserAuditableTests.cs
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class UserAuditableTests
{
    [Fact]
    public void User_ImplementsIAuditable()
    {
        Assert.True(typeof(IAuditable).IsAssignableFrom(typeof(User)));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter UserAuditableTests`
Expected: FAIL — `User` does not implement `IAuditable`.

- [ ] **Step 3: Add the interface to `User`**

In `src/CleanArchCqrs.Domain/Identity/User.cs`, change the class declaration line:

```csharp
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity.Events;

namespace CleanArchCqrs.Domain.Identity;

public sealed class User : AggregateRoot<Guid>, IAuditable
```

(only the `using` list and the class declaration line change — every member below stays as-is.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter UserAuditableTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add src/CleanArchCqrs.Domain/Identity/User.cs tests/CleanArchCqrs.UnitTests/Domain/Identity/UserAuditableTests.cs
git commit -m "feat(domain): mark User as IAuditable"
```

---

### Task 4: Domain — `UserLoginHistory` + `IUserLoginHistoryRepository`

**Files:**
- Create: `src/CleanArchCqrs.Domain/Identity/UserLoginHistory.cs`
- Create: `src/CleanArchCqrs.Domain/Identity/IUserLoginHistoryRepository.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Domain/Identity/UserLoginHistoryTests.cs`

**Interfaces:**
- Consumes: `CleanArchCqrs.Domain.Common.Entity<TId>`
- Produces: `UserLoginHistory.Failed(Guid? userId, string emailAttempted, string failureReason, string? ipAddress, string? userAgent)`, `UserLoginHistory.Succeeded(Guid userId, string emailAttempted, string? ipAddress, string? userAgent)`, and `IUserLoginHistoryRepository.AddAsync(UserLoginHistory record, CancellationToken ct = default)` — consumed by Task 6 (`AppDbContext.UserLoginHistories` and its EF configuration), Task 7 (`UserLoginHistoryRepository`).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CleanArchCqrs.UnitTests/Domain/Identity/UserLoginHistoryTests.cs
using CleanArchCqrs.Domain.Identity;
using Xunit;

namespace CleanArchCqrs.UnitTests.Domain.Identity;

public class UserLoginHistoryTests
{
    [Fact]
    public void Failed_SetsSuccessFalseAndFailureReason()
    {
        var userId = Guid.NewGuid();

        var record = UserLoginHistory.Failed(userId, "a@example.com", "InvalidPassword", "127.0.0.1", "curl/8.0");

        Assert.False(record.Success);
        Assert.Equal("InvalidPassword", record.FailureReason);
        Assert.Equal(userId, record.UserId);
        Assert.Equal("a@example.com", record.EmailAttempted);
    }

    [Fact]
    public void Failed_WithNullUserId_AllowsUnknownEmail()
    {
        var record = UserLoginHistory.Failed(null, "unknown@example.com", "EmailNotFound", null, null);

        Assert.Null(record.UserId);
        Assert.Equal("EmailNotFound", record.FailureReason);
    }

    [Fact]
    public void Succeeded_SetsSuccessTrueAndNoFailureReason()
    {
        var userId = Guid.NewGuid();

        var record = UserLoginHistory.Succeeded(userId, "a@example.com", "127.0.0.1", "curl/8.0");

        Assert.True(record.Success);
        Assert.Null(record.FailureReason);
        Assert.Equal(userId, record.UserId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter UserLoginHistoryTests`
Expected: build error — `UserLoginHistory` not found.

- [ ] **Step 3: Create `UserLoginHistory.cs`**

```csharp
namespace CleanArchCqrs.Domain.Identity;

public sealed class UserLoginHistory : Entity<Guid>
{
    public Guid? UserId { get; private set; }
    public string EmailAttempted { get; private set; } = default!;
    public bool Success { get; private set; }
    public string? FailureReason { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public DateTimeOffset AttemptedAt { get; private set; }

    private UserLoginHistory() { }

    /// Tạo 1 dòng cho lần thử THẤT BẠI — luôn kèm FailureReason.
    public static UserLoginHistory Failed(Guid? userId, string emailAttempted,
        string failureReason, string? ipAddress, string? userAgent)
    {
        return new UserLoginHistory
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            EmailAttempted = emailAttempted,
            Success = false,
            FailureReason = failureReason,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AttemptedAt = DateTimeOffset.UtcNow
        };
    }

    /// Tạo 1 dòng cho lần đăng nhập THÀNH CÔNG — luôn có UserId, không có FailureReason.
    public static UserLoginHistory Succeeded(Guid userId, string emailAttempted,
        string? ipAddress, string? userAgent)
    {
        return new UserLoginHistory
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            EmailAttempted = emailAttempted,
            Success = true,
            FailureReason = null,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            AttemptedAt = DateTimeOffset.UtcNow
        };
    }
}
```

⚠ Do not implement `IAuditable` here, and do not add `Update`/`Delete` methods — this table is insert-only.

- [ ] **Step 4: Create `IUserLoginHistoryRepository.cs`**

```csharp
namespace CleanArchCqrs.Domain.Identity;

public interface IUserLoginHistoryRepository
{
    Task AddAsync(UserLoginHistory record, CancellationToken ct = default);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter UserLoginHistoryTests`
Expected: 3 passed.

- [ ] **Step 6: Verify Domain still has zero packages/references**

Run: `dotnet build src/CleanArchCqrs.Domain`
Expected: 0 error, 0 warning.

- [ ] **Step 7: Commit**

```bash
git add src/CleanArchCqrs.Domain/Identity/UserLoginHistory.cs src/CleanArchCqrs.Domain/Identity/IUserLoginHistoryRepository.cs tests/CleanArchCqrs.UnitTests/Domain/Identity/UserLoginHistoryTests.cs
git commit -m "feat(domain): add UserLoginHistory and its repository contract"
```

---

### Task 5: Infrastructure — `AuditSaveChangesInterceptor`

**Files:**
- Delete: `src/CleanArchCqrs.Infrastructure/Audit/AuditSaveChangesInterceptor.cs` (empty stub, wrong folder)
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptorTests.cs`

**Interfaces:**
- Consumes: `ICurrentUser` (`Application/Common/Interfaces/ICurrentUser.cs`, already exists — `UserId`, `Email`, `Role`, `IsAuthenticated`), `IAuditable`/`AuditAction`/`AuditLog` (Task 2)
- Produces: `AuditSaveChangesInterceptor(ICurrentUser currentUser)` — a `Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor` subclass — consumed by Task 9 (`services.AddScoped<AuditSaveChangesInterceptor>()` + `opt.AddInterceptors(...)`).

This task tests the interceptor against a small **probe `DbContext` defined only in the test file** — it does not depend on Task 6's `AppDbContext`, so it can run before or after Task 6 without conflict.

- [ ] **Step 1: Delete the empty stray file**

```bash
git rm src/CleanArchCqrs.Infrastructure/Audit/AuditSaveChangesInterceptor.cs
```

- [ ] **Step 2: Write the failing tests**

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptorTests.cs
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Constants;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Persistence.Interceptors;

file sealed class FakeCurrentUser : ICurrentUser
{
    public required Guid? UserId { get; init; }
    public string? Email => null;
    public string? Role => null;
    public bool IsAuthenticated => UserId is not null;
}

file sealed class ProbeDbContext : DbContext
{
    public ProbeDbContext(DbContextOptions<ProbeDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    // Real UserConfiguration (Task 6) does this too — without it, EF can't map
    // User.DomainEvents (an AggregateRoot computed property) and model building throws.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
        => modelBuilder.Entity<User>().Ignore(u => u.DomainEvents);
}

public class AuditSaveChangesInterceptorTests
{
    private static ProbeDbContext CreateContext(Guid? currentUserId)
    {
        var interceptor = new AuditSaveChangesInterceptor(new FakeCurrentUser { UserId = currentUserId });
        var options = new DbContextOptionsBuilder<ProbeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;
        return new ProbeDbContext(options);
    }

    [Fact]
    public async Task SaveChanges_OnAddedAuditableEntity_WritesCreatedAuditLog()
    {
        using var context = CreateContext(Guid.NewGuid());
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var log = Assert.Single(context.AuditLogs);
        Assert.Equal(nameof(User), log.EntityName);
        Assert.Equal(user.Id.ToString(), log.EntityId);
        Assert.Equal(AuditAction.Created, log.Action);
        Assert.DoesNotContain("PasswordHash", log.Changes);
    }

    [Fact]
    public async Task SaveChanges_OnModifiedAuditableEntity_WritesUpdatedAuditLogWithOnlyChangedFields()
    {
        using var context = CreateContext(Guid.NewGuid());
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        user.UpdateProfile("Nguyen Van B", null);
        await context.SaveChangesAsync();

        var updateLog = Assert.Single(context.AuditLogs, l => l.Action == AuditAction.Updated);
        Assert.Contains("FullName", updateLog.Changes);
        Assert.DoesNotContain("PasswordHash", updateLog.Changes);
    }

    [Fact]
    public async Task SaveChanges_OnDeletedAuditableEntity_WritesDeletedAuditLog()
    {
        using var context = CreateContext(Guid.NewGuid());
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);
        context.Users.Add(user);
        await context.SaveChangesAsync();

        context.Users.Remove(user);
        await context.SaveChangesAsync();

        var deleteLog = Assert.Single(context.AuditLogs, l => l.Action == AuditAction.Deleted);
        Assert.Equal(user.Id.ToString(), deleteLog.EntityId);
    }

    [Fact]
    public async Task SaveChanges_NoCurrentUser_RecordsNullChangedBy()
    {
        using var context = CreateContext(currentUserId: null);
        var user = User.Create("Seed Admin", "seed@example.com", "hash", null, Roles.Admin);

        context.Users.Add(user);
        await context.SaveChangesAsync();

        var log = Assert.Single(context.AuditLogs);
        Assert.Null(log.ChangedByUserId);
    }
}
```

- [ ] **Step 3: Run tests to verify they fail**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter AuditSaveChangesInterceptorTests`
Expected: build error — `AuditSaveChangesInterceptor` not found.

- [ ] **Step 4: Create `AuditSaveChangesInterceptor.cs`**

```csharp
using System.Text.Json;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CleanArchCqrs.Infrastructure.Persistence.Interceptors;

public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUser _currentUser;

    public AuditSaveChangesInterceptor(ICurrentUser currentUser) => _currentUser = currentUser;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        AddAuditEntries(eventData.Context);
        return base.SavingChangesAsync(eventData, result, ct);
    }

    private void AddAuditEntries(DbContext? context)
    {
        if (context is null) return;

        foreach (var entry in context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable
                     && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted))
        {
            var entityName = entry.Entity.GetType().Name;
            var entityId = entry.Property("Id").CurrentValue?.ToString() ?? "";
            var changes = new Dictionary<string, object?>();

            switch (entry.State)
            {
                case EntityState.Added:
                    foreach (var p in entry.Properties.Where(p => p.Metadata.Name != "PasswordHash"))
                        changes[p.Metadata.Name] = new { New = p.CurrentValue };
                    context.Set<AuditLog>().Add(AuditLog.Create(
                        entityName, entityId, AuditAction.Created, _currentUser.UserId, JsonSerializer.Serialize(changes)));
                    break;

                case EntityState.Modified:
                    foreach (var p in entry.Properties.Where(p => p.IsModified && p.Metadata.Name != "PasswordHash"))
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue, New = p.CurrentValue };
                    if (changes.Count > 0)
                        context.Set<AuditLog>().Add(AuditLog.Create(
                            entityName, entityId, AuditAction.Updated, _currentUser.UserId, JsonSerializer.Serialize(changes)));
                    break;

                case EntityState.Deleted:
                    foreach (var p in entry.Properties.Where(p => p.Metadata.Name != "PasswordHash"))
                        changes[p.Metadata.Name] = new { Old = p.OriginalValue };
                    context.Set<AuditLog>().Add(AuditLog.Create(
                        entityName, entityId, AuditAction.Deleted, _currentUser.UserId, JsonSerializer.Serialize(changes)));
                    break;
            }
        }
    }
}
```

⚠ Override **both** `SavingChanges` and `SavingChangesAsync` — EF picks whichever matches how `SaveChanges`/`SaveChangesAsync` was called; missing one silently drops audit for half the call sites.
⚠ `AuditLog` entities are added to `context.Set<AuditLog>()` **inside** `SavingChanges(Async)`, before the real write happens — EF includes them in the same flush, no second `SaveChanges` call needed.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter AuditSaveChangesInterceptorTests`
Expected: 4 passed.

- [ ] **Step 6: Commit**

```bash
git add src/CleanArchCqrs.Infrastructure/Audit src/CleanArchCqrs.Infrastructure/Persistence/Interceptors tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence/Interceptors
git commit -m "feat(infrastructure): add AuditSaveChangesInterceptor"
```

---

### Task 6: Infrastructure — `AppDbContext` + EF Core configurations

**Files:**
- Modify: `src/CleanArchCqrs.Infrastructure/Persistence/AppDbContext.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Configurations/UserConfiguration.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Configurations/UserLoginHistoryConfiguration.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Persistence/Configurations/AuditLogConfiguration.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence/AppDbContextTests.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence/EntityConfigurationTests.cs`

**Interfaces:**
- Consumes: `IUnitOfWork` (`Domain/Common/IUnitOfWork.cs`, already exists — `Task<int> SaveChangesAsync(CancellationToken ct = default)`), `User`, `UserLoginHistory` (Task 4), `AuditLog` (Task 2)
- Produces: `public class AppDbContext : DbContext, IUnitOfWork` with `DbSet<User> Users`, `DbSet<UserLoginHistory> UserLoginHistories`, `DbSet<AuditLog> AuditLogs`, fully configured via `IEntityTypeConfiguration<T>` classes — consumed by Task 7 (repositories), Task 9 (DI registration).

The current file (`class AppDbContext : DbContext` — no `IUnitOfWork`, no `UserLoginHistories`/`AuditLogs`, inline Fluent API in `OnModelCreating`) is replaced wholesale.

⚠ This task builds the context and its EF configurations together on purpose, not as two tasks: `AppDbContext.OnModelCreating` calls `ApplyConfigurationsFromAssembly`, so a `User` cannot even be saved until `UserConfiguration`'s `Ignore(u => u.DomainEvents)` exists — EF Core cannot map the `AggregateRoot<TId>.DomainEvents` computed property on its own and throws at save time. Splitting these into separate tasks would leave the first one unable to pass its own test.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence/AppDbContextTests.cs
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Constants;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Persistence;

public class AppDbContextTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public void AppDbContext_ImplementsIUnitOfWork()
    {
        using var context = CreateContext();
        Assert.IsAssignableFrom<IUnitOfWork>(context);
    }

    [Fact]
    public async Task SaveChangesAsync_PersistsAddedUser()
    {
        using var context = CreateContext();
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);

        context.Users.Add(user);
        var affected = await context.SaveChangesAsync();

        Assert.Equal(1, affected);
        Assert.Single(context.Users);
    }
}
```

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence/EntityConfigurationTests.cs
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Constants;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Persistence;

public class EntityConfigurationTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public void UserConfiguration_SetsMaxLengthsAndIgnoresDomainEvents()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(User))!;

        Assert.Equal(256, entityType.FindProperty(nameof(User.Email))!.GetMaxLength());
        Assert.Equal(200, entityType.FindProperty(nameof(User.FullName))!.GetMaxLength());
        Assert.Equal(500, entityType.FindProperty(nameof(User.PasswordHash))!.GetMaxLength());
        Assert.Equal(50, entityType.FindProperty(nameof(User.Role))!.GetMaxLength());
        Assert.Null(entityType.FindProperty(nameof(User.DomainEvents)));
    }

    [Fact]
    public async Task UserConfiguration_EnforcesUniqueEmailIndex()
    {
        using var context = CreateContext();
        context.Users.Add(User.Create("A", "dup@example.com", "hash1", null, Roles.Admin));
        await context.SaveChangesAsync();

        context.Users.Add(User.Create("B", "dup@example.com", "hash2", null, Roles.Admin));

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public void AuditLogConfiguration_SetsMaxLengthsAndIndexes()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(AuditLog))!;

        Assert.Equal(100, entityType.FindProperty(nameof(AuditLog.EntityName))!.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(AuditLog.EntityId))!.GetMaxLength());
        Assert.Contains(entityType.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
                { nameof(AuditLog.EntityName), nameof(AuditLog.EntityId), nameof(AuditLog.ChangedAt) }));
        Assert.Contains(entityType.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
                { nameof(AuditLog.ChangedByUserId), nameof(AuditLog.ChangedAt) }));
    }

    [Fact]
    public void UserLoginHistoryConfiguration_SetsMaxLengthsAndIndexes()
    {
        using var context = CreateContext();
        var entityType = context.Model.FindEntityType(typeof(UserLoginHistory))!;

        Assert.Equal(256, entityType.FindProperty(nameof(UserLoginHistory.EmailAttempted))!.GetMaxLength());
        Assert.Equal(100, entityType.FindProperty(nameof(UserLoginHistory.FailureReason))!.GetMaxLength());
        Assert.Contains(entityType.GetIndexes(), i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
                { nameof(UserLoginHistory.UserId), nameof(UserLoginHistory.AttemptedAt) }));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "AppDbContextTests|EntityConfigurationTests"`
Expected: FAIL — `AppDbContext` does not implement `IUnitOfWork` yet, and `Configurations/` doesn't exist yet so max lengths come back `null`, no unique index is enforced, and indexes are missing.

- [ ] **Step 3: Rewrite `AppDbContext.cs`**

```csharp
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Common.Auditing;
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Persistence;

public class AppDbContext : DbContext, IUnitOfWork
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<UserLoginHistory> UserLoginHistories => Set<UserLoginHistory>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
```

⚠ No `ICurrentUser` here — that dependency lives on `AuditSaveChangesInterceptor` (Task 5), not on the context.
⚠ Audit is **not** written here anymore — no override of `SaveChangesAsync`. `DbContext.SaveChangesAsync(CancellationToken)` already matches `IUnitOfWork.SaveChangesAsync(CancellationToken)`'s signature, so no explicit interface implementation is needed either.
⚠ At this point in the task, `ApplyConfigurationsFromAssembly` finds nothing yet (the three configuration classes don't exist until Steps 4–6) — that's expected and is why Step 2's `AppDbContextTests` run was checked together with `EntityConfigurationTests` rather than in isolation.

- [ ] **Step 4: Create `UserConfiguration.cs`**

```csharp
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.HasIndex(u => u.Email).IsUnique();
        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
        builder.Property(u => u.Role).IsRequired().HasMaxLength(50);
        builder.Property(u => u.AvatarUrl).HasMaxLength(500);
        builder.Ignore(u => u.DomainEvents);
    }
}
```

- [ ] **Step 5: Create `UserLoginHistoryConfiguration.cs`**

```csharp
using CleanArchCqrs.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class UserLoginHistoryConfiguration : IEntityTypeConfiguration<UserLoginHistory>
{
    public void Configure(EntityTypeBuilder<UserLoginHistory> builder)
    {
        builder.Property(h => h.EmailAttempted).IsRequired().HasMaxLength(256);
        builder.Property(h => h.FailureReason).HasMaxLength(100);
        builder.Property(h => h.IpAddress).HasMaxLength(64);
        builder.Property(h => h.UserAgent).HasMaxLength(512);
        builder.HasIndex(h => new { h.UserId, h.AttemptedAt });
        builder.HasIndex(h => new { h.EmailAttempted, h.AttemptedAt });
    }
}
```

⚠ No foreign key on `UserId` — it is legitimately `null` when the attempted email doesn't exist, and deleting a `User` must never cascade-delete their login history.

- [ ] **Step 6: Create `AuditLogConfiguration.cs`**

```csharp
using CleanArchCqrs.Domain.Common.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CleanArchCqrs.Infrastructure.Persistence.Configurations;

public sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.Property(a => a.EntityName).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Changes).IsRequired().HasColumnType("jsonb");
        builder.HasIndex(a => new { a.EntityName, a.EntityId, a.ChangedAt });
        builder.HasIndex(a => new { a.ChangedByUserId, a.ChangedAt });
    }
}
```

⚠ `HasColumnType("jsonb")` is a Postgres-only hint; the InMemory provider used in tests ignores it silently, which is why the tests above assert model metadata (max length, indexes) rather than physical column type.

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "AppDbContextTests|EntityConfigurationTests"`
Expected: 6 passed.

- [ ] **Step 8: Commit**

```bash
git add src/CleanArchCqrs.Infrastructure/Persistence tests/CleanArchCqrs.UnitTests/Infrastructure/Persistence
git commit -m "feat(infrastructure): implement AppDbContext with EF Core configurations for User, UserLoginHistory, AuditLog"
```

---

### Task 7: Infrastructure — Repositories

**Files:**
- Create: `src/CleanArchCqrs.Infrastructure/Repositories/UserRepository.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Repositories/UserLoginHistoryRepository.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/Repositories/UserRepositoryTests.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/Repositories/UserLoginHistoryRepositoryTests.cs`

**Interfaces:**
- Consumes: `IUserRepository` (`Domain/Identity/IUserRepository.cs`, already exists — `GetByEmailAsync(string, ct) : Task<User?>`, `GetUserByIdAsync(Guid, ct) : Task<User>`, `AddUserAsync(User, ct) : Task`, `UpdateUser(User) : void`, `EmailExistsAsync(string, ct) : Task<bool>`), `IUserLoginHistoryRepository` (Task 4), `NotFoundException(string message)` (`Domain/Exceptions/NotFoundException.cs`, already exists), `User.NormalizeEmail(string) : string` (already exists), `AppDbContext` (Task 6)
- Produces: `UserRepository`, `UserLoginHistoryRepository` — consumed by Task 9 (DI registration).

⚠ `IUserRepository` in this codebase already exists with the exact member names above — **do not** rename them to match older drafts of the plan (`GetByIdAsync`/`AddAsync`/`Update` do not exist on this interface).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/Repositories/UserRepositoryTests.cs
using CleanArchCqrs.Domain.Constants;
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Repositories;

public class UserRepositoryTests
{
    private static AppDbContext CreateContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString())
        .Options);

    [Fact]
    public async Task GetByEmailAsync_NormalizesEmailBeforeLookup()
    {
        using var context = CreateContext();
        var user = User.Create("Nguyen Van A", "MixedCase@Example.com", "hash", null, Roles.Admin);
        context.Users.Add(user);
        await context.SaveChangesAsync();
        var repo = new UserRepository(context);

        var found = await repo.GetByEmailAsync("mixedcase@example.com");

        Assert.NotNull(found);
        Assert.Equal(user.Id, found!.Id);
    }

    [Fact]
    public async Task GetUserByIdAsync_MissingUser_ThrowsNotFoundException()
    {
        using var context = CreateContext();
        var repo = new UserRepository(context);

        await Assert.ThrowsAsync<NotFoundException>(() => repo.GetUserByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task EmailExistsAsync_NormalizesEmailBeforeLookup()
    {
        using var context = CreateContext();
        context.Users.Add(User.Create("Nguyen Van A", "exists@example.com", "hash", null, Roles.Admin));
        await context.SaveChangesAsync();
        var repo = new UserRepository(context);

        Assert.True(await repo.EmailExistsAsync("EXISTS@example.com"));
        Assert.False(await repo.EmailExistsAsync("nope@example.com"));
    }

    [Fact]
    public async Task AddUserAsync_ThenSaveChanges_PersistsUser()
    {
        using var context = CreateContext();
        var repo = new UserRepository(context);
        var user = User.Create("Nguyen Van A", "new@example.com", "hash", null, Roles.Admin);

        await repo.AddUserAsync(user);
        await context.SaveChangesAsync();

        Assert.Equal(1, context.Users.Count());
    }
}
```

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/Repositories/UserLoginHistoryRepositoryTests.cs
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Repositories;

public class UserLoginHistoryRepositoryTests
{
    [Fact]
    public async Task AddAsync_PersistsRecordOnSaveChanges()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        var repo = new UserLoginHistoryRepository(context);
        var record = UserLoginHistory.Failed(null, "unknown@example.com", "EmailNotFound", "127.0.0.1", "test-agent");

        await repo.AddAsync(record);
        await context.SaveChangesAsync();

        Assert.Single(context.UserLoginHistories);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "UserRepositoryTests|UserLoginHistoryRepositoryTests"`
Expected: build error — `UserRepository`/`UserLoginHistoryRepository` not found.

- [ ] **Step 3: Create `UserRepository.cs`**

```csharp
using CleanArchCqrs.Domain.Exceptions;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CleanArchCqrs.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context) => _context = context;

    public async Task<User?> GetByEmailAsync(string email, CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        return await _context.Users.FirstOrDefaultAsync(u => u.Email == normalized, ct);
    }

    public async Task<User> GetUserByIdAsync(Guid id, CancellationToken ct = default)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id, ct);
        return user ?? throw new NotFoundException($"User with id '{id}' was not found.");
    }

    public async Task AddUserAsync(User user, CancellationToken ct = default)
        => await _context.Users.AddAsync(user, ct);

    public void UpdateUser(User user) => _context.Users.Update(user);

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
    {
        var normalized = User.NormalizeEmail(email);
        return await _context.Users.AnyAsync(u => u.Email == normalized, ct);
    }
}
```

- [ ] **Step 4: Create `UserLoginHistoryRepository.cs`**

```csharp
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;

namespace CleanArchCqrs.Infrastructure.Repositories;

public sealed class UserLoginHistoryRepository : IUserLoginHistoryRepository
{
    private readonly AppDbContext _context;

    public UserLoginHistoryRepository(AppDbContext context) => _context = context;

    public async Task AddAsync(UserLoginHistory record, CancellationToken ct = default)
        => await _context.UserLoginHistories.AddAsync(record, ct);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "UserRepositoryTests|UserLoginHistoryRepositoryTests"`
Expected: 5 passed.

- [ ] **Step 6: Commit**

```bash
git add src/CleanArchCqrs.Infrastructure/Repositories tests/CleanArchCqrs.UnitTests/Infrastructure/Repositories
git commit -m "feat(infrastructure): implement UserRepository and UserLoginHistoryRepository"
```

---

### Task 8: Infrastructure — Security (`PasswordHasher`, `JwtOptions`, `JwtTokenService`)

**Files:**
- Create: `src/CleanArchCqrs.Infrastructure/Security/PasswordHasher.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Security/JwtOptions.cs`
- Create: `src/CleanArchCqrs.Infrastructure/Security/JwtTokenService.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/Security/PasswordHasherTests.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/Security/JwtTokenServiceTests.cs`

**Interfaces:**
- Consumes: `IPasswordHasher` (`Application/Common/Interfaces/IPasswordHasher.cs`, already exists — `Hash(string) : string`, `Verify(string, string) : bool`), `ITokenService` (already exists — `CreateAccessToken(User user) : AccessToken`), `AccessToken` record (already exists — `(string Token, DateTimeOffset ExpiresAtUtc)`)
- Produces: `PasswordHasher`, `JwtOptions { Issuer, Audience, SigningKey, AccessTokenMinutes }`, `JwtTokenService(IOptions<JwtOptions>)` — consumed by Task 9 (DI registration).

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/Security/PasswordHasherTests.cs
using CleanArchCqrs.Infrastructure.Security;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class PasswordHasherTests
{
    [Fact]
    public void Hash_ThenVerify_RoundTripSucceeds()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("P@ssw0rd123");

        Assert.True(hasher.Verify("P@ssw0rd123", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hasher = new PasswordHasher();
        var hash = hasher.Hash("P@ssw0rd123");

        Assert.False(hasher.Verify("wrong-password", hash));
    }
}
```

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/Security/JwtTokenServiceTests.cs
using CleanArchCqrs.Domain.Constants;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.Security;

public class JwtTokenServiceTests
{
    [Fact]
    public void CreateAccessToken_IncludesExpectedClaimsAndExpiry()
    {
        var options = Options.Create(new JwtOptions
        {
            Issuer = "test-issuer",
            Audience = "test-audience",
            SigningKey = new string('k', 32),
            AccessTokenMinutes = 60
        });
        var service = new JwtTokenService(options);
        var user = User.Create("Nguyen Van A", "a@example.com", "hash", null, Roles.Admin);

        var token = service.CreateAccessToken(user);

        var jwt = new JsonWebTokenHandler().ReadJsonWebToken(token.Token);
        Assert.Equal(user.Id.ToString(), jwt.GetClaim(JwtRegisteredClaimNames.Sub).Value);
        Assert.Equal(user.Email, jwt.GetClaim("email").Value);
        Assert.Equal(user.FullName, jwt.GetClaim("name").Value);
        Assert.Equal(Roles.Admin, jwt.GetClaim("role").Value);
        Assert.Equal("test-issuer", jwt.Issuer);
        Assert.True(token.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(59));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "PasswordHasherTests|JwtTokenServiceTests"`
Expected: build error — types not found.

- [ ] **Step 3: Create `PasswordHasher.cs`**

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class PasswordHasher : IPasswordHasher
{
    private readonly Microsoft.AspNetCore.Identity.PasswordHasher<User> _hasher = new();

    public string Hash(string password) => _hasher.HashPassword(default!, password);

    public bool Verify(string password, string passwordHash)
    {
        var result = _hasher.VerifyHashedPassword(default!, passwordHash, password);
        return result is PasswordVerificationResult.Success or PasswordVerificationResult.SuccessRehashNeeded;
    }
}
```

⚠ `PasswordVerificationResult.SuccessRehashNeeded` is still a **correct** password — comparing only against `.Success` would reject valid logins whenever ASP.NET Core Identity's default hashing parameters change.

- [ ] **Step 4: Create `JwtOptions.cs`**

```csharp
namespace CleanArchCqrs.Infrastructure.Security;

public sealed class JwtOptions
{
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 60;
}
```

- [ ] **Step 5: Create `JwtTokenService.cs`**

```csharp
using System.Text;
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Application.Common.Models;
using CleanArchCqrs.Domain.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CleanArchCqrs.Infrastructure.Security;

public sealed class JwtTokenService : ITokenService
{
    private readonly JwtOptions _options;

    public JwtTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public AccessToken CreateAccessToken(User user)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(_options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
                SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                ["email"] = user.Email,
                ["name"] = user.FullName,
                ["role"] = user.Role,
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString()
            }
        };

        var token = new JsonWebTokenHandler().CreateToken(descriptor);
        return new AccessToken(token, expires);
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter "PasswordHasherTests|JwtTokenServiceTests"`
Expected: 3 passed.

- [ ] **Step 7: Commit**

```bash
git add src/CleanArchCqrs.Infrastructure/Security tests/CleanArchCqrs.UnitTests/Infrastructure/Security
git commit -m "feat(infrastructure): implement PasswordHasher and JwtTokenService"
```

---

### Task 9: Infrastructure — DI wiring + Postgres connection string cleanup

**Files:**
- Modify: `src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`
- Modify: `src/CleanArchCqrs.Infrastructure/CleanArchCqrs.Infrastructure.csproj` (remove unused SqlServer package)
- Modify: `src/CleanArchCqrs.API/appsettings.json` (Postgres-style connection string)
- Test: `tests/CleanArchCqrs.UnitTests/Infrastructure/DependencyInjection/InfrastructureServiceExtensionsTests.cs`

**Interfaces:**
- Consumes: `AuditSaveChangesInterceptor` (Task 5), `AppDbContext` (Task 6), `IUserRepository`/`UserRepository`, `IUserLoginHistoryRepository`/`UserLoginHistoryRepository` (Task 7), `IPasswordHasher`/`PasswordHasher`, `ITokenService`/`JwtTokenService`, `JwtOptions` (Task 8), `ICurrentUser` (already exists, registered by the API project — not by this task)
- Produces: `AddInfrastructureServices(this IServiceCollection, IConfiguration)` fully wired — consumed by `Program.cs` in both API (already calls it) and by Task 10/11 indirectly (nothing new for them).

- [ ] **Step 1: Write the failing test**

```csharp
// tests/CleanArchCqrs.UnitTests/Infrastructure/DependencyInjection/InfrastructureServiceExtensionsTests.cs
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.DependencyInjection;
using CleanArchCqrs.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace CleanArchCqrs.UnitTests.Infrastructure.DependencyInjection;

file sealed class FakeCurrentUser : ICurrentUser
{
    public Guid? UserId => null;
    public string? Email => null;
    public string? Role => null;
    public bool IsAuthenticated => false;
}

public class InfrastructureServiceExtensionsTests
{
    [Fact]
    public void AddInfrastructureServices_RegistersAllContracts()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Jwt:SigningKey"] = new string('k', 32),
                ["Jwt:AccessTokenMinutes"] = "60"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddScoped<ICurrentUser, FakeCurrentUser>();
        services.AddInfrastructureServices(configuration);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<AppDbContext>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IUserLoginHistoryRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPasswordHasher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ITokenService>());
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter InfrastructureServiceExtensionsTests`
Expected: FAIL — most of these services are not yet registered.

- [ ] **Step 3: Rewrite `InfrastructureServiceExtensions.cs`**

```csharp
using CleanArchCqrs.Application.Common.Interfaces;
using CleanArchCqrs.Domain.Common;
using CleanArchCqrs.Domain.Identity;
using CleanArchCqrs.Infrastructure.Persistence;
using CleanArchCqrs.Infrastructure.Persistence.Interceptors;
using CleanArchCqrs.Infrastructure.Repositories;
using CleanArchCqrs.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace CleanArchCqrs.Infrastructure.DependencyInjection;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructureServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<AuditSaveChangesInterceptor>();

        services.AddDbContext<AppDbContext>((sp, opt) =>
        {
            opt.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
            opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
        });

        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IUserLoginHistoryRepository, UserLoginHistoryRepository>();
        services.AddScoped<IPasswordHasher, PasswordHasher>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

        return services;
    }
}
```

⚠ `AuditSaveChangesInterceptor` must be registered `Scoped` — it depends on `ICurrentUser`, which is scoped per request. `Singleton` would freeze the first request's user for every later request.
⚠ Keep the `(sp, opt) => ...` lambda form — the previous `(opt) => ...` form used by the current file cannot resolve `AuditSaveChangesInterceptor` from the container.

- [ ] **Step 4: Remove the unused SQL Server package**

```bash
dotnet remove src/CleanArchCqrs.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
```

- [ ] **Step 5: Fix the API's connection string to Postgres syntax**

In `src/CleanArchCqrs.API/appsettings.json`, replace:

```json
"ConnectionStrings": {
  "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=CleanArchCqrsDb;Trusted_Connection=True;"
}
```

with:

```json
"ConnectionStrings": {
  "DefaultConnection": "Host=localhost;Port=5432;Database=CleanArchCqrsDb;Username=postgres;Password=postgres"
}
```

(adjust `Username`/`Password` to match whatever Postgres instance you run locally — this only needs to be a syntactically valid Npgsql string for the DI smoke test in this task; nothing here opens a real connection.)

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter InfrastructureServiceExtensionsTests`
Expected: PASS. Resolving `AppDbContext` does not open a network connection, so this passes without a running Postgres server.

- [ ] **Step 7: Run the full test suite and full solution build**

```bash
dotnet test tests/CleanArchCqrs.UnitTests
dotnet build
```

Expected: all tests green, 0 build errors/warnings across the solution.

- [ ] **Step 8: Commit**

```bash
git add src/CleanArchCqrs.Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs src/CleanArchCqrs.Infrastructure/CleanArchCqrs.Infrastructure.csproj src/CleanArchCqrs.API/appsettings.json tests/CleanArchCqrs.UnitTests/Infrastructure/DependencyInjection
git commit -m "feat(infrastructure): wire audit interceptor and login services into DI, drop unused SqlServer package"
```

---

### Task 10: Logging — Serilog for API and Gateway

**Files:**
- Modify: `src/CleanArchCqrs.API/CleanArchCqrs.API.csproj`, `src/CleanArchCqrs.API/appsettings.json`, `src/CleanArchCqrs.API/appsettings.Development.json`, `src/CleanArchCqrs.API/Program.cs`
- Modify: `src/CleanArchCqrs.Gateway/CleanArchCqrs.Gateway.csproj`, `src/CleanArchCqrs.Gateway/appsettings.json`, `src/CleanArchCqrs.Gateway/appsettings.Development.json`, `src/CleanArchCqrs.Gateway/Program.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `builder.Host.UseSerilog(...)` wired in both projects, so `ILogger<T>` (already used by `LoggingBehavior.cs`) now writes through Serilog. Task 11 depends on this for `UseSerilogRequestLogging()` and `Serilog.Context.LogContext`.

This task has no unit-testable surface — it is hosting/config wiring. Verification is `dotnet build` plus a manual run, matching how the rest of this codebase verifies startup wiring (there is no test project pattern for `Program.cs` in this repo).

- [ ] **Step 1: Add Serilog packages to both projects**

```bash
dotnet add src/CleanArchCqrs.API package Serilog.AspNetCore
dotnet add src/CleanArchCqrs.API package Serilog.Sinks.File
dotnet add src/CleanArchCqrs.API package Serilog.Settings.Configuration

dotnet add src/CleanArchCqrs.Gateway package Serilog.AspNetCore
dotnet add src/CleanArchCqrs.Gateway package Serilog.Sinks.File
dotnet add src/CleanArchCqrs.Gateway package Serilog.Settings.Configuration
```

(`Serilog.AspNetCore` already depends on `Serilog.Sinks.Console` — no separate console package needed.)

- [ ] **Step 2: Add the `Serilog` section to `src/CleanArchCqrs.API/appsettings.json`**

Add this top-level key alongside the existing `Logging`/`AllowedHosts`/`ConnectionStrings` keys:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft": "Warning",
      "Microsoft.EntityFrameworkCore": "Warning"
    }
  },
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "File", "Args": { "path": "logs/api-.log", "rollingInterval": "Day", "retainedFileCountLimit": 14 } }
  ],
  "Enrich": [ "FromLogContext" ]
}
```

- [ ] **Step 3: Add a Development override to `src/CleanArchCqrs.API/appsettings.Development.json`**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

- [ ] **Step 4: Wire Serilog into `src/CleanArchCqrs.API/Program.cs`**

Add right after `var builder = WebApplication.CreateBuilder(args);` (before the existing `builder.Services.AddControllers()...` block):

```csharp
using Serilog;
```

at the top of the file, and:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));
```

- [ ] **Step 5: Add the `Serilog` section to `src/CleanArchCqrs.Gateway/appsettings.json`**

Add this top-level key alongside the existing `Logging`/`AllowedHosts`/`ReverseProxy` keys:

```json
"Serilog": {
  "MinimumLevel": {
    "Default": "Information",
    "Override": {
      "Microsoft": "Warning",
      "Yarp": "Warning"
    }
  },
  "WriteTo": [
    { "Name": "Console" },
    { "Name": "File", "Args": { "path": "logs/gateway-.log", "rollingInterval": "Day", "retainedFileCountLimit": 14 } }
  ],
  "Enrich": [ "FromLogContext" ]
}
```

- [ ] **Step 6: Add a Development override — `src/CleanArchCqrs.Gateway/appsettings.Development.json`**

This file does not exist yet in the Gateway project. Create it:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Debug",
      "Microsoft.AspNetCore": "Information",
      "Yarp": "Information"
    }
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Debug"
    }
  }
}
```

- [ ] **Step 7: Wire Serilog into `src/CleanArchCqrs.Gateway/Program.cs`**

Add `using Serilog;` at the top, and after `var builder = WebApplication.CreateBuilder(args);`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));
```

(this goes before the existing `builder.Services.AddGatewayReverseProxy(builder.Configuration);` line.)

- [ ] **Step 8: Build both projects**

```bash
dotnet build src/CleanArchCqrs.API
dotnet build src/CleanArchCqrs.Gateway
```

Expected: 0 error, 0 warning for both.

- [ ] **Step 9: Manually verify logging output**

```bash
dotnet run --project src/CleanArchCqrs.API
```

In another terminal, hit any endpoint (e.g. `curl http://localhost:5289/swagger/v1/swagger.json`), then check:
- Console shows structured log lines with timestamp + level.
- A file appears at `src/CleanArchCqrs.API/logs/api-<date>.log` with the same content.

Repeat for the Gateway (`dotnet run --project src/CleanArchCqrs.Gateway`, expect `logs/gateway-<date>.log`).

- [ ] **Step 10: Commit**

```bash
git add src/CleanArchCqrs.API/CleanArchCqrs.API.csproj src/CleanArchCqrs.API/appsettings.json src/CleanArchCqrs.API/appsettings.Development.json src/CleanArchCqrs.API/Program.cs
git add src/CleanArchCqrs.Gateway/CleanArchCqrs.Gateway.csproj src/CleanArchCqrs.Gateway/appsettings.json src/CleanArchCqrs.Gateway/appsettings.Development.json src/CleanArchCqrs.Gateway/Program.cs
git commit -m "feat(logging): wire Serilog console+file sinks into API and Gateway"
```

---

### Task 11: Correlation ID middleware (Gateway + API)

**Files:**
- Create: `src/CleanArchCqrs.Gateway/Middleware/CorrelationIdMiddleware.cs`
- Modify: `src/CleanArchCqrs.Gateway/Program.cs`
- Create: `src/CleanArchCqrs.API/Middleware/CorrelationIdMiddleware.cs`
- Modify: `src/CleanArchCqrs.API/Program.cs`
- Test: `tests/CleanArchCqrs.UnitTests/Gateway/Middleware/CorrelationIdMiddlewareTests.cs`
- Test: `tests/CleanArchCqrs.UnitTests/API/Middleware/CorrelationIdMiddlewareTests.cs`

**Interfaces:**
- Consumes: `Serilog.Context.LogContext` (Task 10's `Serilog.AspNetCore` package), `Microsoft.AspNetCore.Http.RequestDelegate`/`HttpContext`
- Produces: two independent `CorrelationIdMiddleware` classes (one per project, since Gateway references no other project in this solution) that read/generate an `X-Correlation-Id` header — nothing later consumes these types directly; this is the last task in the plan.

Both middleware classes are intentionally identical in logic and duplicated per project — the Global Constraints forbid the Gateway from referencing any other project.

- [ ] **Step 1: Write the failing tests (same shape for both projects)**

```csharp
// tests/CleanArchCqrs.UnitTests/Gateway/Middleware/CorrelationIdMiddlewareTests.cs
using CleanArchCqrs.Gateway.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanArchCqrs.UnitTests.Gateway.Middleware;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoExistingHeader_GeneratesIdAndSetsRequestAndResponseHeaders()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey("X-Correlation-Id"));
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-Id"));
        Assert.Equal(
            context.Request.Headers["X-Correlation-Id"].ToString(),
            context.Response.Headers["X-Correlation-Id"].ToString());
        Assert.True(Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out _));
    }

    [Fact]
    public async Task InvokeAsync_ExistingHeader_PreservesValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "existing-id-123";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("existing-id-123", context.Response.Headers["X-Correlation-Id"].ToString());
    }
}
```

```csharp
// tests/CleanArchCqrs.UnitTests/API/Middleware/CorrelationIdMiddlewareTests.cs
using CleanArchCqrs.API.Middleware;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CleanArchCqrs.UnitTests.API.Middleware;

public class CorrelationIdMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoExistingHeader_GeneratesIdAndSetsRequestAndResponseHeaders()
    {
        var context = new DefaultHttpContext();
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey("X-Correlation-Id"));
        Assert.True(context.Response.Headers.ContainsKey("X-Correlation-Id"));
        Assert.Equal(
            context.Request.Headers["X-Correlation-Id"].ToString(),
            context.Response.Headers["X-Correlation-Id"].ToString());
        Assert.True(Guid.TryParse(context.Request.Headers["X-Correlation-Id"], out _));
    }

    [Fact]
    public async Task InvokeAsync_ExistingHeader_PreservesValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers["X-Correlation-Id"] = "existing-id-123";
        var middleware = new CorrelationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("existing-id-123", context.Response.Headers["X-Correlation-Id"].ToString());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter CorrelationIdMiddlewareTests`
Expected: build error — `CorrelationIdMiddleware` not found in either namespace.

- [ ] **Step 3: Create `src/CleanArchCqrs.Gateway/Middleware/CorrelationIdMiddleware.cs`**

```csharp
namespace CleanArchCqrs.Gateway.Middleware;

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Request.Headers[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
```

⚠ Setting `context.Request.Headers[HeaderName]` (not just the response) is what makes YARP forward this value downstream — YARP forwards all incoming request headers by default, no `RequestTransform` needed.
⚠ Response header is set **before** calling `_next(context)` — safe because the response hasn't started; setting it after `_next` can throw once the response has begun writing.

- [ ] **Step 4: Create `src/CleanArchCqrs.API/Middleware/CorrelationIdMiddleware.cs`**

Same code, different namespace:

```csharp
namespace CleanArchCqrs.API.Middleware;

public sealed class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Request.Headers[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test tests/CleanArchCqrs.UnitTests --filter CorrelationIdMiddlewareTests`
Expected: 4 passed.

- [ ] **Step 6: Wire the middleware into `src/CleanArchCqrs.Gateway/Program.cs`**

Add `using CleanArchCqrs.Gateway.Middleware;` at the top. Change:

```csharp
        var app = builder.Build();

        app.MapReverseProxy();

        app.Run();
```

to:

```csharp
        var app = builder.Build();

        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();

        app.MapReverseProxy();

        app.Run();
```

- [ ] **Step 7: Wire the middleware into `src/CleanArchCqrs.API/Program.cs`**

Add `using CleanArchCqrs.API.Middleware;` at the top. Change:

```csharp
        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();

        app.Run();
```

to:

```csharp
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseSerilogRequestLogging();

        app.UseHttpsRedirection();
        app.UseAuthorization();
        app.MapControllers();

        app.Run();
```

⚠ `CorrelationIdMiddleware` must run **first** in both pipelines — any middleware added later (e.g. a future `GlobalExceptionHandlerMiddleware` from a different plan) goes **after** it, so even early failures are logged with a correlation ID.
⚠ `UseSerilogRequestLogging()` goes right after `CorrelationIdMiddleware` and before everything else — it wraps the rest of the pipeline, so its one-line summary log captures the final status code even if something downstream changes it.

- [ ] **Step 8: Build the full solution**

```bash
dotnet build
```

Expected: 0 error, 0 warning.

- [ ] **Step 9: Manually verify correlation ID propagation**

```bash
dotnet run --project src/CleanArchCqrs.API &
dotnet run --project src/CleanArchCqrs.Gateway &
curl -i http://localhost:5100/api/auth/does-not-exist
```

Expected: response has an `X-Correlation-Id` header; the same value appears in both `src/CleanArchCqrs.Gateway/logs/gateway-*.log` and `src/CleanArchCqrs.API/logs/api-*.log` for that request. Re-run with `curl -i -H "X-Correlation-Id: manual-test-id" ...` and confirm `manual-test-id` is the value that shows up in both logs unchanged.

- [ ] **Step 10: Run the full test suite one last time**

```bash
dotnet test tests/CleanArchCqrs.UnitTests
```

Expected: all tests passed.

- [ ] **Step 11: Commit**

```bash
git add src/CleanArchCqrs.Gateway/Middleware src/CleanArchCqrs.Gateway/Program.cs src/CleanArchCqrs.API/Middleware src/CleanArchCqrs.API/Program.cs tests/CleanArchCqrs.UnitTests/Gateway/Middleware tests/CleanArchCqrs.UnitTests/API/Middleware
git commit -m "feat(logging): add correlation ID middleware to Gateway and API"
```

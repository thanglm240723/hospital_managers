# Luồng Audit & Logging — plan đầy đủ

File này tự chứa toàn bộ việc cần làm cho: ghi `AuditLog` (ai sửa gì) qua `ISaveChangesInterceptor`,
và logging hệ thống (Serilog + correlation ID + request logging) xuyên Gateway → API.
Quy ước chung xem [00-quyet-dinh-va-quy-uoc.md](00-quyet-dinh-va-quy-uoc.md).

> **Thay thế Phần 5.0.1–5.0.3 của [luong-login.md](luong-login.md).** Bản gốc ghi audit bằng cách
> override `AppDbContext.SaveChangesAsync`; plan này đổi sang `ISaveChangesInterceptor` — tách khỏi
> `AppDbContext`, để `AppDbContext` không phình to khi Phần 8 (luong-login.md) thêm domain-event dispatch.
> `UserLoginHistory` (Phần 5.0.4–5.0.5 luong-login.md) **không đổi**, không thuộc phạm vi file này.

## Điểm xuất phát — trạng thái code hiện tại

| File | Trạng thái |
|---|---|
| `Domain/Audit/AuditLog.cs` | Rỗng, tạo nhầm chỗ — sẽ xoá, dựng lại ở `Domain/Common/Auditing/` |
| `Infrastructure/Audit/AuditSaveChangesInterceptor.cs` | Rỗng, tạo nhầm chỗ — sẽ xoá, dựng lại ở `Infrastructure/Persistence/Interceptors/` |
| `Infrastructure/Persistence/AppDbContext.cs` | Đã có, nhưng chỉ có `DbSet<User>`, chưa implement `IUnitOfWork`, chưa nhận `ICurrentUser` — phần đó dựng ở `luong-login.md` Phần 5.1, **không lặp lại ở đây** |
| `Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs` | Có sẵn dòng comment `//opt.AddInterceptors(sp.GetRequiredService<DomainEventDispatchInterceptor>());` — đúng hướng interceptor đã chọn, bật lại cùng lúc với `AuditSaveChangesInterceptor` |
| `Application/Common/Behaviors/LoggingBehavior.cs` | ✅ đã xong, dùng `ILogger<T>` chuẩn — Serilog cắm vào **không cần sửa file này** |
| `Domain/Identity/User.cs` | Chưa implement `IAuditable` |

## Quyết định riêng của luồng này

| Vấn đề | Chốt | Lý do |
|---|---|---|
| Cách ghi Audit | `ISaveChangesInterceptor` (EF Core), không override `SaveChangesAsync` | Tách trách nhiệm khỏi `AppDbContext`, không đụng code khi Phần 8 thêm domain-event dispatch |
| Vị trí `AuditLog` | `Domain/Common/Auditing/` | Audit là hạ tầng dùng chung (như `Entity`, `AggregateRoot`), không phải feature nghiệp vụ |
| Logging framework | Serilog thay `ILogger` mặc định | Structured logging, cấu hình sink qua `appsettings.json` không cần sửa code |
| Log sink | Console + File (rolling theo ngày) | Đủ cho giai đoạn 1 API + 1 Gateway chạy trên máy dev, chưa cần hạ tầng thêm (Seq/ELK) |
| Correlation ID | Sinh ở Gateway, forward xuống API qua header `X-Correlation-Id` | Gateway là cửa vào duy nhất; tra được 1 request xuyên cả 2 tiến trình log riêng biệt |
| Request logging | `Serilog.AspNetCore` `UseSerilogRequestLogging()` | 1 dòng log/request có sẵn status code + thời gian, không cần viết middleware tay; khác tầng với `LoggingBehavior` (log theo command/query MediatR) |

---

# Phần A — Domain: hợp đồng Audit

⚠ Domain luật ở mục B `00-quyet-dinh-va-quy-uoc.md`: 0 package, 0 project reference.

**→ Xoá** `Domain/Audit/AuditLog.cs` (file rỗng, sai vị trí).

### A.1 — `Domain/Common/Auditing/IAuditable.cs`

Marker interface, không có member. Entity nào implement mới bị `AuditLog` ghi lại — tránh audit mặc
định mọi bảng, tránh tự audit chính `AuditLog`/`UserLoginHistory`.

```csharp
namespace CleanArchCqrs.Domain.Common.Auditing;

/// Entity implement interface này sẽ được AuditSaveChangesInterceptor tự ghi vào AuditLog mỗi khi Add/Modify/Delete.
public interface IAuditable
{
}
```

### A.2 — `Domain/Common/Auditing/AuditAction.cs`

```csharp
namespace CleanArchCqrs.Domain.Common.Auditing;

public enum AuditAction
{
    Created,
    Updated,
    Deleted
}
```

### A.3 — `Domain/Common/Auditing/AuditLog.cs`

Entity thuần — **không** kế thừa `AggregateRoot<TId>` (không raise domain event), chỉ `Entity<Guid>`
để có Id + equality. Insert-only, không có method sửa sau khi tạo.

| Cột | Kiểu | Ghi chú |
|---|---|---|
| `Id` | `Guid` | `Guid.CreateVersion7()` |
| `EntityName` | `string` | `typeof(TEntity).Name`, ví dụ `"User"` |
| `EntityId` | `string` | Lưu dạng string để dùng chung cho mọi entity dù PK là `Guid` hay kiểu khác sau này |
| `Action` | `AuditAction` | Created / Updated / Deleted |
| `ChangedByUserId` | `Guid?` | `null` = hệ thống (seed, migration data) |
| `ChangedAt` | `DateTimeOffset` | UTC |
| `Changes` | `string` | JSON: `{"FieldName":{"Old":..,"New":..}}` — chỉ field thật sự đổi |

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

⚠ `AuditLog` **không** implement `IAuditable` — nếu implement, mỗi lần ghi audit lại tự sinh thêm 1 dòng
audit cho chính nó, phình vô hạn.

### A.4 — Sửa `Domain/Identity/User.cs`

```csharp
public sealed class User : AggregateRoot<Guid>, IAuditable
```

✓ **Xong Phần A khi:** `dotnet build src/CleanArchCqrs.Domain` — 0 error, 0 warning, vẫn 0 `<PackageReference>`.

---

# Phần B — Infrastructure: `AuditSaveChangesInterceptor`

**→ Xoá** `Infrastructure/Audit/AuditSaveChangesInterceptor.cs` (file rỗng, sai vị trí).

### B.1 — `Infrastructure/Persistence/Interceptors/AuditSaveChangesInterceptor.cs`

Kế thừa `Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor`. Override cả bản đồng bộ
lẫn bất đồng bộ — EF gọi bản nào tuỳ nơi gọi `SaveChanges`/`SaveChangesAsync`, bỏ sót 1 bản là audit
im lặng biến mất ở nửa số trường hợp.

```csharp
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
            // giống BuildAuditLogs() — xem 4 nhánh Created/Modified/Deleted, lọc PasswordHash, ⚠ bên dưới
        }
    }
}
```

⚠ **`SavingChanges`/`SavingChangesAsync` chạy TRƯỚC khi `SaveChanges` thật sự thực thi** — entity `Added`
vẫn đọc được `entry.Property("Id").CurrentValue` vì PK sinh client-side (`Guid.CreateVersion7()`, đã
chốt ở `00-quyet-dinh-va-quy-uoc.md`). Nếu sau này có entity dùng PK do DB tự sinh (identity/serial),
`CurrentValue` ở bước này sẽ là giá trị mặc định (0/null) — audit `EntityId` sẽ sai, cần xử lý riêng.
⚠ Thêm entity audit mới ngay trong `AddAuditEntries` bằng `context.Set<AuditLog>().Add(...)` — **không**
gọi `context.SaveChanges()` lại ở đây, EF sẽ gộp các entity mới thêm vào cùng lượt save đang chạy.
⚠ **Loại `PasswordHash` khỏi `Changes`** giống bản gốc — nếu sau này entity khác có field nhạy cảm
(vd `Patient.InsuranceCode`) thì nhớ lọc thêm ở đây.
⚠ `DomainEvents` là property tính toán trên `AggregateRoot`, phải `Ignore` ở configuration (Phần 5.2
`luong-login.md`) — nếu không EF coi nó là cột và phá vỡ cả `ChangeTracker.Entries()`.

### B.2 — Đăng ký trong `Infrastructure/DependencyInjection/InfrastructureServiceExtensions.cs`

```csharp
services.AddScoped<AuditSaveChangesInterceptor>();

services.AddDbContext<AppDbContext>((sp, opt) =>
{
    opt.UseNpgsql(configuration.GetConnectionString("DefaultConnection"));
    opt.AddInterceptors(sp.GetRequiredService<AuditSaveChangesInterceptor>());
});
```

⚠ Interceptor phải đăng ký `Scoped` — nó phụ thuộc `ICurrentUser` (cũng scoped theo request). Đăng ký
`Singleton` sẽ giữ nguyên `ICurrentUser` của request đầu tiên cho mọi request sau.
⚠ Dòng comment cũ `//opt.AddInterceptors(sp.GetRequiredService<DomainEventDispatchInterceptor>());` —
khi Phần 8 (`luong-login.md`) làm domain-event dispatch, đăng ký **thêm** một lời gọi `AddInterceptors`
nữa (hoặc truyền nhiều interceptor cùng lúc: `opt.AddInterceptors(a, b)`), không thay thế dòng của audit.

### B.3 — `AppDbContext` chỉ cần thêm 1 dòng

`Persistence/AppDbContext.cs` (dựng đầy đủ ở `luong-login.md` Phần 5.1) chỉ cần khai thêm:

```csharp
public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
```

Không cần override `SaveChangesAsync` cho audit nữa — interceptor tự chạy mỗi khi `SaveChanges`/
`SaveChangesAsync` được gọi trên context, dù gọi từ đâu.

✓ **Xong Phần B khi:** `dotnet build` — 0 error, 0 warning.

---

# Phần C — Infrastructure: EF configuration cho `AuditLog`

**`Persistence/Configurations/AuditLogConfiguration.cs`**

| Cột | Cấu hình |
|---|---|
| `EntityName` | required, `HasMaxLength(100)` |
| `EntityId` | required, `HasMaxLength(100)` |
| `Changes` | required, `HasColumnType("jsonb")` — Postgres, query được theo field JSON sau này nếu cần |
| Index | `(EntityName, EntityId, ChangedAt)` |
| Index | `(ChangedByUserId, ChangedAt)` |

⚠ Không FK `ChangedByUserId → Users.Id` bắt buộc — giá trị `null` hợp lệ cho hành động hệ thống (seed),
và xoá `User` không được phép kéo theo xoá lịch sử audit của chính nó.

---

# Phần D — Logging: cài Serilog

### D.1 — Package (cả API và Gateway — 2 tiến trình riêng, mỗi bên tự log)

```bash
dotnet add src/CleanArchCqrs.API package Serilog.AspNetCore
dotnet add src/CleanArchCqrs.API package Serilog.Sinks.File
dotnet add src/CleanArchCqrs.API package Serilog.Settings.Configuration

dotnet add src/CleanArchCqrs.Gateway package Serilog.AspNetCore
dotnet add src/CleanArchCqrs.Gateway package Serilog.Sinks.File
dotnet add src/CleanArchCqrs.Gateway package Serilog.Settings.Configuration
```

`Serilog.AspNetCore` đã kéo theo sink Console — không cần thêm `Serilog.Sinks.Console` riêng.

### D.2 — `appsettings.json` — thêm section `Serilog` (khác nhau ở `path` giữa 2 project)

**API:**
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

**Gateway:** giống hệt, chỉ đổi `"path": "logs/gateway-.log"`.

⚠ Kiểm tra `.gitignore` đã có `logs/` chưa — file log không commit vào git.

### D.3 — Sửa `Program.cs` (cả 2 project) — thêm ngay sau `CreateBuilder`

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));
```

⚠ `LoggingBehavior.cs` (Application) và mọi `ILogger<T>` khác **không cần sửa gì** — Serilog thay thế
provider logging mặc định của ASP.NET Core, code gọi `ILogger<T>` giữ nguyên.

✓ **Xong Phần D khi:** chạy `dotnet run` ở API — console hiện log dạng có timestamp + level, file
`logs/api-<ngày>.log` xuất hiện sau request đầu tiên.

---

# Phần E — Correlation ID + request logging

### E.1 — `Gateway/Middleware/CorrelationIdMiddleware.cs`

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

⚠ Set vào `context.Request.Headers` (không chỉ response) — đây là cách để YARP forward giá trị này
xuống API mà **không cần** cấu hình `RequestTransform` riêng: YARP mặc định forward nguyên vẹn mọi
header của request đang xử lý.
⚠ Set response header **trước** khi gọi `_next(context)` — an toàn vì response chưa bắt đầu ghi; nếu
đặt sau `_next()` mà nhánh nào đó đã flush response thì sẽ ném lỗi "Headers are read-only".

**→ Sửa `Gateway/Program.cs`:**

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();   // đầu tiên
app.UseSerilogRequestLogging();
app.MapReverseProxy();
```

### E.2 — `API/Middleware/CorrelationIdMiddleware.cs`

Giống hệt logic Gateway (copy sang namespace `CleanArchCqrs.API.Middleware`) — đọc lại header nếu
Gateway đã set, tự sinh nếu gọi thẳng API lúc dev (bỏ qua Gateway).

**→ Sửa `API/Program.cs`** — thứ tự trong pipeline (kết hợp với `GlobalExceptionHandlerMiddleware` sẽ
thêm ở `luong-login.md` Phần 6):

```csharp
app.UseMiddleware<CorrelationIdMiddleware>();            // 1. đầu tiên — mọi log sau đều có CorrelationId
app.UseSerilogRequestLogging();                          // 2. log 1 dòng/request, có status code CUỐI CÙNG
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();    // 3. Phần 6 luong-login.md — thêm khi làm tới đó
app.UseHttpsRedirection();
app.UseAuthentication();                                  // Phần 6
app.UseAuthorization();
app.MapControllers();
```

⚠ `CorrelationIdMiddleware` phải là middleware **đầu tiên** — kể cả trước exception handler. Nếu đặt
sau, request lỗi ngay từ đầu pipeline sẽ log thiếu `CorrelationId`.
⚠ `UseSerilogRequestLogging()` đặt **trước** `GlobalExceptionHandlerMiddleware` — nó bọc toàn bộ pipeline
phía sau nên dòng log tổng kết ghi được status code cuối cùng, kể cả khi exception handler đã map
exception → status code (404/401/409/500).
⚠ Route hiện tại của Gateway forward nguyên header — không cần sửa `appsettings.json` của
`ReverseProxy` cho việc này.

✓ **Xong Phần E khi:**
- Gọi API **qua Gateway**, không tự set header → response có `X-Correlation-Id`, và cùng giá trị đó
  xuất hiện trong cả `logs/gateway-*.log` lẫn `logs/api-*.log` cho cùng 1 request.
- Gọi kèm sẵn header `X-Correlation-Id: abc-123` → log giữ nguyên `abc-123`, không sinh giá trị mới.
- Console/File có dòng log của `UseSerilogRequestLogging` — gồm method, path, status code, elapsed ms.

---

# ✓ Nghiệm thu chung

- [ ] `dotnet build` toàn solution — 0 error, 0 warning
- [ ] `CleanArchCqrs.Domain.csproj` vẫn 0 `<PackageReference>` dù đã thêm `AuditLog`, `IAuditable`, `AuditAction`
- [ ] Sửa 1 field của `User` (vd đổi `FullName`) → bảng `AuditLogs` có 1 dòng `EntityName = "User"`,
      `Action = Updated`, `Changes` chứa đúng field đã đổi, **không** chứa `PasswordHash`
- [ ] Đăng nhập (đổi `LastLoginAt`/`UpdatedAt`) → 1 dòng `AuditLogs` tương ứng (đã ghi trong nghiệm thu
      của `luong-login.md`, giờ ghi qua interceptor thay vì override `SaveChangesAsync`)
- [ ] Log console/file không chứa mật khẩu thô hay `PasswordHash` ở bất kỳ dòng nào
- [ ] 1 request qua Gateway → cùng `CorrelationId` xuất hiện ở log Gateway và log API
- [ ] Tắt/bật lại app → file log cũ vẫn còn (không bị ghi đè), file mới theo ngày tạo riêng

# Ngoài phạm vi — ghi backlog

- API cho admin xem `AuditLog` (query, phân trang, filter theo `EntityName`/`ChangedByUserId`) — làm
  khi có màn hình admin, dùng `PagedQuery`/`PagedResult` đã chốt ở `00-quyet-dinh-va-quy-uoc.md`
- Seq/ELK cho log tập trung, tìm kiếm qua UI — nâng cấp khi cần, chỉ đổi `WriteTo` trong `appsettings.json`
- Sampling / giảm mức log khi traffic lớn
- Correlation ID cho log phía Frontend (React) gửi kèm request — nối chuỗi truy vết Browser → Gateway → API

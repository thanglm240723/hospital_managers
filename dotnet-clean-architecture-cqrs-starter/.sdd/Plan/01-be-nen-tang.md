# 01 — Backend: nền tảng

Mục tiêu: dựng lại bộ khung dùng chung cho mọi module. Chưa có nghiệp vụ nào.

## Bước 1.1 — Domain: lớp nền

**→ Tạo `src/CleanArchCqrs.Domain/Common/BaseEntity.cs`**

```csharp
public abstract class BaseEntity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
}
```

**→ Tạo `src/CleanArchCqrs.Domain/Exceptions/DomainException.cs`**
Base exception + 2 lớp con dùng khắp nơi:
- `NotFoundException(string entityName, object key)` → API map thành 404
- `BusinessRuleViolationException(string message)` → API map thành 409

⚠ Mỗi exception 1 file riêng, đúng quy ước "1 file 1 type".

**→ Tạo `src/CleanArchCqrs.Domain/Common/IAggregateRoot.cs`**
Interface rỗng đánh dấu aggregate root. Chỉ aggregate root mới có repository.
Trong hệ này: `Patient`, `Doctor`, `Appointment`, `MedicalRecord`, `Invoice`, `User`.

✓ **Xong khi:** `dotnet build src/CleanArchCqrs.Domain` thành công, project vẫn 0 package reference.

## Bước 1.2 — Application: model dùng chung

**→ Tạo `Common/Models/PagedResult.cs`**

```csharp
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
```

⚠ Template gốc định nghĩa `PagedResult<T>` **2 lần** (Domain và Application). Chỉ giữ **1 bản ở Application**.
Repository bên Domain trả `IReadOnlyList<T>` + `int totalCount` riêng, đừng trả `PagedResult`.

**→ Tạo `Common/Models/PagedQuery.cs`** — base record cho mọi query có phân trang:

```csharp
public abstract record PagedQuery
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SearchTerm { get; init; }
}
```

Kèm hàm chuẩn hoá: `PageNumber < 1 → 1`, `PageSize` kẹp trong `[1, 100]`.

**→ Tạo `Common/Exceptions/ValidationException.cs`**
Nhận `IEnumerable<ValidationFailure>` của FluentValidation, phơi ra
`IDictionary<string, string[]> Errors` (key = tên property) để API trả về đúng chuẩn.

**→ Tạo `Common/Interfaces/ICurrentUser.cs`**

```csharp
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? UserName { get; }
    IReadOnlyList<string> Roles { get; }
    bool IsInRole(string role);
}
```

Application cần biết "ai đang thao tác" (để ghi `CreatedBy`, để check quyền nghiệp vụ) nhưng
**không được** `using Microsoft.AspNetCore.Http`. Interface đặt ở đây, implement ở API (bước 1.6).

## Bước 1.3 — Application: pipeline behaviors

**→ Tạo `Common/Behaviors/ValidationBehavior.cs`**

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    // Handle: chạy song song mọi validator qua Task.WhenAll,
    // gom Errors != null; nếu có lỗi -> throw ValidationException; không thì return await next();
}
```

**→ Tạo `Common/Behaviors/LoggingBehavior.cs`**
Log tên request + thời gian xử lý (`Stopwatch`) + log Warning nếu > 500ms.
⚠ **Không log toàn bộ request** — chứa dữ liệu bệnh nhân và mật khẩu.

**→ Sửa `DependencyInjection/ApplicationServiceExtensions.cs`**

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceExtensions).Assembly));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
services.AddValidatorsFromAssembly(typeof(ApplicationServiceExtensions).Assembly);
```

⚠ **Lỗi của template gốc:** nó viết `services.AddTransient(typeof(LoggingBehavior<,>))` — đăng ký
class chứ không đăng ký vào `IPipelineBehavior<,>`, nên behavior **không bao giờ chạy**. Phải viết như trên.
⚠ Thứ tự đăng ký = thứ tự chạy. `Logging` trước `Validation` để log được cả request bị chặn.

## Bước 1.4 — Infrastructure: persistence

**→ Tạo `Persistence/AppDbContext.cs`**
- `DbSet<T>` cho từng aggregate root (thêm dần theo phase 2–4).
- `OnModelCreating`: `modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);`
- Global query filter soft delete cho **từng** entity: `HasQueryFilter(e => !e.IsDeleted)`.
- Override `SaveChangesAsync`: tự set `CreatedAt/CreatedBy/UpdatedAt/UpdatedBy` bằng `ICurrentUser`,
  và đổi `EntityState.Deleted` → `Modified` + `IsDeleted = true`.

**→ Tạo `Persistence/Configurations/<Entity>Configuration.cs`** (thêm dần)
Mỗi entity 1 file implement `IEntityTypeConfiguration<T>`: độ dài cột, required, index, quan hệ.

**→ Sửa `DependencyInjection/InfrastructureServiceExtensions.cs`**

```csharp
services.AddDbContext<AppDbContext>(o => o.UseSqlServer(connectionString));
// đăng ký repository ở đây khi tạo tới
```

⚠ Template gốc **bỏ qua** tham số `connectionString` và hardcode `UseInMemoryDatabase`. Phải dùng thật.

## Bước 1.5 — Migration đầu tiên

Chạy sau khi có ít nhất 1 entity (làm ở phase 2):

```bash
dotnet ef migrations add InitialCreate \
  --project src/CleanArchCqrs.Infrastructure \
  --startup-project src/CleanArchCqrs.API
dotnet ef database update \
  --project src/CleanArchCqrs.Infrastructure \
  --startup-project src/CleanArchCqrs.API
```

⚠ `--project` là nơi chứa DbContext, `--startup-project` là nơi có `Program.cs`. Thiếu 1 trong 2 là lỗi.

## Bước 1.6 — API: middleware & wiring

**→ Tạo `Middleware/GlobalExceptionHandlerMiddleware.cs`**
Bắt exception, map sang **RFC 7807 Problem Details**:

| Exception | Status | `type` |
|---|---|---|
| `ValidationException` | 400 | `validation-error`, kèm `errors` |
| `NotFoundException` | 404 | `not-found` |
| `BusinessRuleViolationException` | 409 | `business-rule-violation` |
| `UnauthorizedAccessException` | 403 | `forbidden` |
| còn lại | 500 | `internal-error` — **log full, response chỉ trả message chung** |

⚠ Không bao giờ trả `ex.ToString()` ra response ở môi trường Production.

**→ Tạo `Services/CurrentUser.cs`** — implement `ICurrentUser`, đọc từ `IHttpContextAccessor.HttpContext.User`.

**→ Sửa `Program.cs`**
Thêm vào, đúng thứ tự:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
// ... (JWT thêm ở phase 2)

app.UseMiddleware<GlobalExceptionHandlerMiddleware>();   // ĐẦU TIÊN trong pipeline
app.UseHttpsRedirection();
app.UseAuthentication();                                  // phase 2
app.UseAuthorization();
app.MapControllers();
```

⚠ **Lỗi của template gốc:** middleware có tồn tại nhưng **không hề được `UseMiddleware`**. Nhớ thêm dòng này.
⚠ `UseAuthentication()` phải đứng **trước** `UseAuthorization()`, sai thứ tự thì `User` luôn rỗng.

✓ **Xong phase 1 khi:**
- `dotnet build` 0 error.
- Gọi thử 1 endpoint ném `NotFoundException` → nhận đúng JSON Problem Details 404.
- Log hiện dòng thời gian xử lý của `LoggingBehavior` (chứng minh pipeline đã gắn).

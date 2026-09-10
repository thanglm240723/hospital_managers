# Luồng Login — plan đầy đủ từ Domain đến API

File này tự chứa toàn bộ việc cần làm để `POST /api/auth/login` chạy được end-to-end.
Quy ước chung (đặt tên, chiều phụ thuộc, URL) xem [00-quyet-dinh-va-quy-uoc.md](00-quyet-dinh-va-quy-uoc.md).

> Plan này đã đối chiếu với code thật tại commit `9496ffb`. Chỗ nào code đã có thì ghi rõ **đã có**,
> không viết lại.

## Điểm xuất phát — Domain đã có gì

| File | Nội dung | Trạng thái |
|---|---|---|
| `Domain/Common/Entity.cs` | `Entity<TId>` — Id, equality theo type + Id | ✅ dùng được luôn |
| `Domain/Common/AggregateRoot.cs` | `AggregateRoot<TId>` — `Raise()`, `DomainEvents`, `ClearDomainEvents()` | ✅ |
| `Domain/Common/IDomainEvent.cs` | `IDomainEvent` + `abstract record DomainEvent` | ✅ |
| `Domain/Identity/User.cs` | `User : AggregateRoot<Guid>` — factory `Create`, `ChangePassword`, `UpdateProfile`, `Activate`, `Deactivate` | ⚠ còn 2 lỗi, xem Phần 0 |
| `Domain/Identity/Events/*.cs` | 5 domain event | ✅ nhưng **chưa có gì dispatch** — xem Phần 8 |
| `Domain/Constants/Role.cs` | `Role` — User, Admin, Doctor, Nurse | ⚠ đổi tên, xem Phần 0 |

**Không dùng `BaseEntity` / `IAggregateRoot`.** Plan cũ đề xuất 2 thứ này nhưng code đã đi hướng khác
và hướng hiện tại tốt hơn: rich domain model, private setter, factory method. Giữ nguyên.

## Quyết định riêng của luồng login

| Vấn đề | Chốt | Lý do |
|---|---|---|
| Định danh đăng nhập | **Email** | `User` không có `UserName`. Thêm vào lúc này là sửa Domain vô ích |
| Kiểu thời gian | `DateTimeOffset` | Theo code đã viết, không đổi sang `DateTime` |
| Soft delete cho `User` | **Không làm** | `IsActive` đã đủ để khoá tài khoản. `IsDeleted` để dành cho `Patient` / `MedicalRecord` |
| `CreatedBy` / `UpdatedBy` | **Hoãn** | Cần `ICurrentUser` trong DbContext, chưa cần cho login. Thêm khi làm module Patients |
| Refresh token | **Không làm** | Access token 60 phút, hết hạn thì đăng nhập lại. Ghi backlog |

---

# Phần 0 — Sửa Domain trước

⚠ **Hai lỗi 0.1 và 0.2 sẽ chặn luồng login.** Sửa trước khi code tầng khác, nếu không sẽ mất rất nhiều
thời gian debug mà không hiểu tại sao đăng nhập luôn thất bại.

**→ Sửa `Domain/Identity/User.cs`**

### 0.1 — `IsActive` không bao giờ bằng `true`

Constructor không gán `IsActive`, mà `default(bool)` là `false`. Mọi user tạo qua `Create()` đều bị khoá.
Handler login chặn ở bước kiểm tra `IsActive` → **admin seed xong không đăng nhập được**.

```csharp
private User(Guid id, string fullName, string email, string passwordHash, string? avatarUrl, string role)
{
    // ... các gán hiện có
    IsActive = true;          // ← thêm dòng này
}
```

### 0.2 — Không có cách nào ghi `LastLoginAt`

`LastLoginAt` là `private set` nhưng `User` chưa có method nào set nó. Handler login không ghi nhận được.
Thêm method — không đổi property thành public setter, làm vậy là phá encapsulation.

```csharp
public void RecordLogin()
{
    LastLoginAt = DateTimeOffset.UtcNow;
    Touch();
}
```

### 0.3 — Đổi tên `Roles` → `Role`

Property giữ **một** giá trị nhưng đang đặt tên số nhiều. Đồng thời đổi class hằng
`Constants/Role.cs` thành `Roles` để không trùng tên:

```csharp
// Domain/Constants/Roles.cs   (đổi tên cả file)
public static class Roles
{
    public const string Admin        = "Admin";
    public const string Doctor       = "Doctor";
    public const string Nurse        = "Nurse";
    public const string Receptionist = "Receptionist";
    public const string Accountant   = "Accountant";
}
```

⚠ Bỏ hằng `User = "User"` — bệnh viện không có vai trò tên "User". Bổ sung `Receptionist`, `Accountant`
vì nghiệp vụ tiếp nhận và thu ngân cần tới.

Đọc thành `Roles.Admin` (số nhiều = tập hằng), gán vào `user.Role` (số ít = một giá trị). Rõ nghĩa hơn hẳn.

### 0.4 — Chuẩn hoá email

`Create()` đang `.Trim()` cho `fullName` nhưng bỏ qua `email` — gõ sao lưu vậy.

**Hậu quả nặng nhất là lúc đăng nhập:** đăng ký bằng `"Quan@Gmail.com"`, hôm sau gõ `"quan@gmail.com"`
thì `GetByEmailAsync` trả `null` → 401, mà người dùng chắc chắn mình gõ đúng email.

**Đặt việc chuẩn hoá ở Domain, không ở handler.** Bất biến "hai user không trùng email" đã bao gồm
"không phân biệt hoa thường" — đó là định nghĩa nghiệp vụ, không phải chuyện định dạng chuỗi. Để ở handler
thì mỗi caller mới (seed admin, import CSV, tool admin) đều phải *nhớ* gọi, quên một chỗ là có bản ghi bẩn.

Thêm static method vào `User` để cả lúc ghi lẫn lúc đọc dùng chung một phép biến đổi:

```csharp
public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();
```

`Create()` gọi nó khi dựng entity **và** khi raise `UserRegisteredDomainEvent`.
`UserRepository.GetByEmailAsync()` cũng gọi đúng method này — chuẩn hoá lúc ghi chỉ có tác dụng nếu
lúc đọc dùng y hệt.

⚠ `ToLowerInvariant()` chứ không phải `ToLower()`. Với culture `tr-TR`, `"I".ToLower()` ra `"ı"` (i không chấm)
→ cùng một email lại không khớp nhau tuỳ máy chạy.

**Phân vai với tầng trên:** validator bên Application lo "có phải email không" (`.EmailAddress()`, trả 400 kèm
message thân thiện); Domain chỉ lo chuẩn hoá + chặn rỗng. Đừng nhét regex validate đầy đủ vào Domain —
`ArgumentException` cho ra message xấu, đó là việc của validator.

Value object `Email` là bậc trên nữa (không thể tạo ra email chưa chuẩn hoá, repository nhận `Email` thay
`string`). Để dành làm cùng module Patients, nơi sẽ có thêm `PhoneNumber`, `InsuranceCode` cùng khuôn.

⚠ Đừng trông chờ unique index bắt hộ. SQL Server collation mặc định (`..._CI_AS`) bỏ qua hoa/thường và
khoảng trắng **đuôi**, nhưng khoảng trắng **đầu** thì vẫn lọt. Quan trọng hơn: so sánh chuỗi trong C#
(`user.Email == input`) luôn phân biệt hoa thường bất kể collation, provider `InMemory` dùng khi test cũng vậy,
và Postgres thì phân biệt mặc định. Đúng đắn không nên phụ thuộc vào một cấu hình của DB.

### 0.5 — Đóng lại setter công khai

Hai property này đang lệch chuẩn so với phần còn lại của entity (`FullName`, `Email`, `IsActive`...
đều `private set`):

```csharp
public DateTimeOffset CreatedAt { get; private set; }        // bỏ public set
public DateTimeOffset? UpdatedAt { get; private set; }
```

Bạn đã thiết kế `UpdatedAt` chỉ đổi qua `Touch()`, mà `Touch()` chỉ được gọi bên trong method nghiệp vụ.
Public setter phá vỡ ý đồ đó — bất kỳ handler hay controller nào cũng viết được
`user.CreatedAt = ...` / `user.UpdatedAt = null`, khiến hai mốc thời gian này lệch khỏi
thời điểm dữ liệu thực sự thay đổi. Với hồ sơ bệnh viện, mốc tạo bản ghi phải đáng tin mới dùng làm
bằng chứng được.

⚠ Không sợ EF không ghi được — EF Core set property qua reflection, `private set` hoàn toàn bình thường.
Đó chính là lý do `FullName` và `Email` để `private set` mà vẫn chạy.

### 0.6 — Hết cảnh báo CS8618

`Role` là non-nullable nhưng constructor rỗng (dành cho EF) không gán. Thêm `= default!;` như các property khác.

✓ **Xong Phần 0 khi:** `dotnet build src/CleanArchCqrs.Domain` — 0 error, 0 warning.

---

# Phần 1 — Package cần cài ✅ ĐÃ XONG

> Đã chạy xong, không cần làm lại. Giữ lại đây để tra khi dựng máy mới.

```bash
cd D:/hospital_management/dotnet-clean-architecture-cqrs-starter

dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer --version 10.0.12
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.EntityFrameworkCore.Design   --version 10.0.12
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.Extensions.Identity.Core
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.IdentityModel.JsonWebTokens

dotnet add src/CleanArchCqrs.API package Microsoft.AspNetCore.Authentication.JwtBearer

dotnet tool install --global dotnet-ef     # 1 lần cho máy
```

⚠ Toàn bộ EF Core + `Microsoft.Extensions.*` đã đưa về **`10.0.12`** cho khớp TFM `net10.0`.
Bản gốc pin `9.0.4`, nhưng `Microsoft.Extensions.Identity.Core` kéo `Microsoft.Extensions.*` 10.x xuống nên
sinh lỗi `NU1605` (package downgrade). Giữ mọi package `Microsoft.*` cùng dòng 10.x.

⚠ Không cài package nào vào `CleanArchCqrs.Domain`. Domain phải giữ 0 package.

---

# Phần 2 — Domain: bổ sung hợp đồng

5 file, đều ngắn. Xong phần này là Domain đóng lại hoàn toàn, từ Phần 3 mới sang Application.

Nhắc lại quy ước áp dụng cho cả 5 file: file-scoped namespace, 1 file 1 type, XML doc `///` trên
public type và public method, method bất đồng bộ có hậu tố `Async` và nhận `CancellationToken ct = default`.

### 2.1 — `Domain/Identity/IUserRepository.cs`

Namespace `CleanArchCqrs.Domain.Identity` — đặt **cạnh `User.cs`**, không tách ra `Domain/Interfaces/`.
Feature folder: cái gì thuộc về Identity thì nằm trong Identity.

| Method | Trả về | Ghi chú |
|---|---|---|
| `GetByEmailAsync(string email, ct)` | `User?` | Dùng cho login. Phải trả instance **có tracking** |
| `GetByIdAsync(Guid id, ct)` | `User?` | Dùng cho `GET /me` |
| `EmailExistsAsync(string email, ct)` | `bool` | Dùng cho `Register`, chưa cần ở luồng login |
| `AddAsync(User user, ct)` | `Task` | Chỉ stage, chưa ghi DB |
| `Update(User user)` | `void` | **Không** `async` |

⚠ `Update` trả `void` chứ không phải `Task`: EF chỉ đánh dấu entity state trong bộ nhớ, không có I/O nào
xảy ra. Trả `Task` ở đây là nói dối về hành vi của method, và kéo theo `await` vô nghĩa ở mọi handler.

⚠ Repository **không** có `SaveChangesAsync`. Việc đó thuộc `IUnitOfWork` — xem 2.2.

### 2.2 — `Domain/Common/IUnitOfWork.cs`

```csharp
Task<int> SaveChangesAsync(CancellationToken ct = default);
```

Tách khỏi repository để một handler ghi nhiều aggregate trong **một** transaction. Nếu mỗi repository tự
`SaveChanges` thì handler nào đụng 2 bảng sẽ tạo ra 2 transaction rời — hỏng một nửa là dữ liệu lệch.

`AppDbContext` sẽ implement interface này ở Phần 5, và đây chính là chỗ publish domain event ở Phần 8.

### 2.3–2.5 — `Domain/Exceptions/`

Mỗi type 1 file, namespace `CleanArchCqrs.Domain.Exceptions`.

| File | Kiểu | Chữ ký | API map |
|---|---|---|---|
| `DomainException.cs` | `abstract class : Exception` | `protected DomainException(string message)` | — |
| `NotFoundException.cs` | `sealed class : DomainException` | `(string entityName, object key)` | 404 |
| `BusinessRuleViolationException.cs` | `sealed class : DomainException` | `(string message)` | 409 |

`NotFoundException` nên giữ lại `EntityName` và `Key` thành property, đồng thời tự dựng message dạng
`"{entityName} with key '{key}' was not found."` — middleware log được cấu trúc, không phải parse chuỗi.

**Tại sao cần lớp cha `DomainException`:** middleware ở Phần 6 phân biệt được "lỗi nghiệp vụ đã lường trước"
với "lỗi ngoài dự kiến" bằng một câu `catch (DomainException)`, thay vì liệt kê từng type và quên mất
type mới thêm sau này. Lỗi nghiệp vụ trả message thật cho client; lỗi ngoài dự kiến chỉ trả message chung.

⚠ Login **không** dùng `NotFoundException` khi không tìm thấy email. Phải ném `UnauthorizedAccessException`
giống hệt trường hợp sai mật khẩu — xem cảnh báo bảo mật ở Phần 4.

✓ **Xong Phần 2 khi:**
- `dotnet build` — 0 error
- `Domain.csproj` vẫn **0** `<PackageReference>` và **0** `<ProjectReference>`

---

# Phần 3 — Application: nền tảng dùng chung

**→ Tạo `Common/Interfaces/IPasswordHasher.cs`**

```csharp
string Hash(string password);
bool Verify(string password, string passwordHash);
```

**→ Tạo `Common/Interfaces/ITokenService.cs`**

```csharp
AccessToken CreateAccessToken(User user);
```

**→ Tạo `Common/Models/AccessToken.cs`** — `record AccessToken(string Token, DateTimeOffset ExpiresAtUtc);`

**→ Tạo `Common/Interfaces/ICurrentUser.cs`**

```csharp
Guid? UserId { get; }
string? Email { get; }
string? Role { get; }
bool IsAuthenticated { get; }
```

⚠ Application cần biết "ai đang thao tác" nhưng **không được** `using Microsoft.AspNetCore.Http`.
Interface khai ở đây, implement bên API (Phần 6).

**→ Tạo `Common/Exceptions/ValidationException.cs`**
Nhận `IEnumerable<ValidationFailure>` của FluentValidation, phơi ra `IDictionary<string, string[]> Errors`.

**→ Tạo `Common/Behaviors/ValidationBehavior.cs`**

```csharp
public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    // chạy mọi validator, gom failure, có lỗi -> throw ValidationException, không -> return await next();
}
```

**→ Tạo `Common/Behaviors/LoggingBehavior.cs`**
Log tên request + thời gian xử lý (`Stopwatch`), Warning nếu > 500ms.

⚠ **Tuyệt đối không log nội dung request.** `LoginCommand` chứa mật khẩu thô — log ra là mật khẩu
nằm vĩnh viễn trong file log. Chỉ log `typeof(TRequest).Name`.

**→ Sửa `DependencyInjection/ApplicationServiceExtensions.cs`**

```csharp
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationServiceExtensions).Assembly));
services.AddValidatorsFromAssembly(typeof(ApplicationServiceExtensions).Assembly);
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
```

⚠ Phải đăng ký vào `IPipelineBehavior<,>`, không phải `AddTransient(typeof(LoggingBehavior<,>))` —
đăng ký kiểu sau thì behavior **không bao giờ chạy**.
⚠ Thứ tự đăng ký = thứ tự chạy. `Logging` trước `Validation` để log được cả request bị chặn.

---

# Phần 4 — Application: use-case Login

```
Application/Auth/
├── Commands/Login/
│   ├── LoginCommand.cs
│   └── LoginCommandHandler.cs
├── Validators/
│   └── LoginCommandValidator.cs
└── Models/
    ├── LoginResult.cs   (Token, ExpiresAtUtc, User)
    └── UserDto.cs       (Id, Email, FullName, Role, AvatarUrl)
```

**→ Tạo `LoginCommand.cs`** — `record LoginCommand(string Email, string Password) : IRequest<LoginResult>;`

**→ Tạo `UserDto.cs`**

⚠ **Không bao giờ** có `PasswordHash` trong DTO. Map thủ công từng field, đừng dùng AutoMapper kiểu
"map hết cho nhanh" — một ngày nào đó sẽ lộ hash ra response.

**→ Tạo `LoginCommandHandler.cs`** — thứ tự xử lý:

1. `GetByEmailAsync(email.Trim().ToLowerInvariant())` → không thấy: ném `UnauthorizedAccessException`
2. `user.IsActive == false` → ném `UnauthorizedAccessException`
3. `_hasher.Verify(password, user.PasswordHash)` sai → ném `UnauthorizedAccessException`
4. `user.RecordLogin()` → `_repo.Update(user)` → `_uow.SaveChangesAsync(ct)`
5. `_tokenService.CreateAccessToken(user)` → trả `LoginResult`

⚠ **Ba nhánh 1–3 phải trả về cùng một message**: `"Email hoặc mật khẩu không đúng."`
Phân biệt "email không tồn tại" với "sai mật khẩu" cho phép kẻ tấn công dò ra danh sách tài khoản có thật.

⚠ Bước 4 chạy **trước** bước 5. Nếu `SaveChangesAsync` lỗi thì không phát token. Đừng đảo thứ tự.

**→ Tạo `LoginCommandValidator.cs`** — `Email` bắt buộc + đúng định dạng, `Password` bắt buộc.

⚠ Validator của login **không** kiểm tra độ mạnh mật khẩu. Quy tắc đó thuộc về `RegisterCommand`.
Áp vào login sẽ chặn luôn user cũ có mật khẩu đặt theo quy tắc cũ.

---

# Phần 5 — Infrastructure: cài đặt thật

**→ Tạo `Persistence/AppDbContext.cs`** — `DbContext`, `IUnitOfWork`

```csharp
public DbSet<User> Users => Set<User>();

protected override void OnModelCreating(ModelBuilder b)
    => b.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
```

⚠ `DomainEvents` là property tính toán trên `AggregateRoot`, EF sẽ tưởng nó là cột. Phải `Ignore` nó
trong cấu hình của **từng** entity. Không dùng `[NotMapped]` vì attribute đó nằm ở Domain — vi phạm
quy tắc Domain 0 package.

**→ Tạo `Persistence/Configurations/UserConfiguration.cs`** — `IEntityTypeConfiguration<User>`

| Cột | Cấu hình |
|---|---|
| `Email` | required, `HasMaxLength(256)`, **unique index** |
| `FullName` | required, `HasMaxLength(200)` |
| `PasswordHash` | required, `HasMaxLength(500)` |
| `Role` | required, `HasMaxLength(50)` |
| `AvatarUrl` | nullable, `HasMaxLength(500)` |
| `DomainEvents` | `Ignore` |

⚠ Không đặt `[Table]`, `[Key]`, `[MaxLength]` lên entity. Tất cả cấu hình bằng Fluent API ở đây.

**→ Tạo `Repositories/UserRepository.cs`** — implement `IUserRepository`

⚠ `GetByEmailAsync` dùng cho login **cần tracking** (vì sau đó gọi `RecordLogin()` rồi save).
Không thêm `AsNoTracking()` vào method này. Chỉ dùng `AsNoTracking()` cho query thuần đọc.
⚠ Normalize email trong repository y hệt cách `User.Create()` normalize, nếu không sẽ không khớp.

**→ Tạo `Security/PasswordHasher.cs`**
Bọc `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2, tự sinh salt riêng mỗi bản ghi).

⚠ Tuyệt đối không tự viết `SHA256(password)`. Không salt, tính quá nhanh — vỡ trong vài giờ nếu lộ DB.
⚠ `VerifyHashedPassword` trả `PasswordVerificationResult` có 3 giá trị. `SuccessRehashNeeded` cũng là **đúng**,
đừng chỉ so sánh với `Success`.

**→ Tạo `Security/JwtOptions.cs`**

```csharp
public string Issuer { get; set; } = "";
public string Audience { get; set; } = "";
public string SigningKey { get; set; } = "";      // >= 32 ký tự cho HS256
public int AccessTokenMinutes { get; set; } = 60;
```

**→ Tạo `Security/JwtTokenService.cs`** — implement `ITokenService`, dùng `JsonWebTokenHandler`

| Claim | Giá trị |
|---|---|
| `sub` | `user.Id` |
| `email` | `user.Email` |
| `name` | `user.FullName` |
| `role` | `user.Role` |
| `jti` | `Guid.NewGuid()` |
| `iss` / `aud` / `exp` / `iat` | từ `JwtOptions` |

**→ Sửa `DependencyInjection/InfrastructureServiceExtensions.cs`**

```csharp
public static IServiceCollection AddInfrastructureServices(
    this IServiceCollection services, IConfiguration configuration)
{
    services.AddDbContext<AppDbContext>(o =>
        o.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

    services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<AppDbContext>());
    services.AddScoped<IUserRepository, UserRepository>();
    services.AddScoped<IPasswordHasher, PasswordHasher>();
    services.AddScoped<ITokenService, JwtTokenService>();
    services.Configure<JwtOptions>(configuration.GetSection("Jwt"));

    return services;
}
```

⚠ Chữ ký hiện tại nhận `string? connectionString`. Đổi sang `IConfiguration` để bind được `JwtOptions`,
rồi sửa lời gọi trong `Program.cs` thành `AddInfrastructureServices(builder.Configuration)`.
⚠ Method hiện đang rỗng (chỉ có `return services;`) — phải **thay**, không phải thêm vào cuối.

---

# Phần 6 — API: bật xác thực

**→ Sửa `appsettings.json`** — thêm section `Jwt`:

```json
"Jwt": {
  "Issuer": "http://localhost:5289",
  "Audience": "hospital-management",
  "SigningKey": "",
  "AccessTokenMinutes": 60
}
```

⚠ `SigningKey` để **rỗng** trong file commit. Cấp qua user-secrets:

```bash
dotnet user-secrets init --project src/CleanArchCqrs.API
dotnet user-secrets set "Jwt:SigningKey" "<chuỗi ngẫu nhiên >= 32 ký tự>" --project src/CleanArchCqrs.API
```

Thêm fail-fast lúc khởi động nếu key rỗng — thà chết lúc start còn hơn chạy với token ai cũng ký được.

**→ Tạo `Services/CurrentUser.cs`** — implement `ICurrentUser`, đọc từ `IHttpContextAccessor`.

**→ Tạo `Middleware/GlobalExceptionHandlerMiddleware.cs`** — map sang RFC 7807 Problem Details:

| Exception | Status |
|---|---|
| `ValidationException` | 400, kèm `errors` |
| `UnauthorizedAccessException` | **401** |
| `NotFoundException` | 404 |
| `BusinessRuleViolationException` | 409 |
| còn lại | 500 — log full, response chỉ trả message chung |

⚠ Login sai trả **401** (chưa xác thực được), không phải 403 (đã biết là ai nhưng không đủ quyền).
⚠ Không bao giờ trả `ex.ToString()` ra response ở Production.

**→ Tạo `Controllers/AuthController.cs`** — `[Route("api/auth")]`, `[Authorize]` ở cấp class

| Method | Route | Attribute | Trả về |
|---|---|---|---|
| POST | `login` | `[AllowAnonymous]` | `LoginResult` |
| GET | `me` | `[Authorize]` | `UserDto` |

⚠ Đặt `[Authorize]` ở class rồi mở `[AllowAnonymous]` cho từng action — an toàn hơn làm ngược lại,
vì quên gắn `[Authorize]` cho action mới thì nó vẫn được bảo vệ.
⚠ Route phải đúng `api/auth` để khớp `auth-route` mà Gateway đã cấu hình sẵn.

**→ Sửa `Program.cs`** — đúng thứ tự:

```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, CurrentUser>();
builder.Services.AddApplicationServices();
builder.Services.AddInfrastructureServices(builder.Configuration);

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidateAudience = true,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            // Issuer / Audience / IssuerSigningKey lấy từ JwtOptions
        };
    });
builder.Services.AddAuthorization();

// --- pipeline ---
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();   // ĐẦU TIÊN
app.UseHttpsRedirection();
app.UseAuthentication();                                  // trước Authorization
app.UseAuthorization();
app.MapControllers();
```

⚠ `MapInboundClaims = false` để claim giữ nguyên tên `sub` / `role`, không bị .NET đổi thành URI dài.
Quên dòng này thì `[Authorize(Roles = ...)]` và `ICurrentUser.UserId` đều không đọc được.
⚠ `UseAuthentication()` phải **trước** `UseAuthorization()`, sai thứ tự thì `User` luôn rỗng.
⚠ Swagger cần `AddSecurityDefinition` kiểu Bearer mới test được endpoint có `[Authorize]`.

---

# Phần 7 — Migration & seed tài khoản Admin

```bash
dotnet ef migrations add InitialCreate \
  --project src/CleanArchCqrs.Infrastructure \
  --startup-project src/CleanArchCqrs.API

dotnet ef database update \
  --project src/CleanArchCqrs.Infrastructure \
  --startup-project src/CleanArchCqrs.API
```

⚠ `--project` là nơi chứa DbContext, `--startup-project` là nơi có `Program.cs`. Thiếu 1 trong 2 là lỗi.

**→ Tạo `Infrastructure/Persistence/DbInitializer.cs`**
Chạy lúc khởi động: `MigrateAsync()`, nếu chưa có user nào thì tạo 1 Admin bằng `User.Create(...)`
với `Roles.Admin`, mật khẩu lấy từ config `Seed:AdminPassword`.

⚠ Không hardcode mật khẩu admin trong source. Đặt qua user-secrets như `Jwt:SigningKey`.
⚠ Seed **phải** đi qua `User.Create()` chứ không `new User()` — để mật khẩu được hash và
`IsActive = true` (Phần 0.1). Insert thẳng bằng SQL sẽ tạo ra tài khoản không đăng nhập được.

---

# Phần 8 — Dispatch domain event (làm sau cùng)

5 event trong `Domain/Identity/Events/` hiện **chưa chạy**. `Raise()` chỉ thêm vào `List` trong entity.
Luồng login vẫn hoạt động đầy đủ mà không cần phần này — làm sau khi login đã chạy được.

**Vấn đề cần giải:** MediatR publish qua `INotification`, nhưng `IDomainEvent` **không được** kế thừa
`INotification` — làm vậy là kéo package MediatR vào Domain, vi phạm quy tắc "Domain 0 package".

**Cách làm:** wrapper ở Application.

```csharp
// Application/Common/Events/DomainEventNotification.cs
public record DomainEventNotification<T>(T DomainEvent) : INotification where T : IDomainEvent;
```

Handler viết dạng `INotificationHandler<DomainEventNotification<UserRegisteredDomainEvent>>`.

**→ Sửa `AppDbContext.SaveChangesAsync`**

```
1. gom entity đang track có DomainEvents.Count > 0
2. copy danh sách event ra biến ngoài
3. gọi ClearDomainEvents() trên từng entity
4. await base.SaveChangesAsync(ct)          ← lưu DB trước
5. publish từng event qua IPublisher        ← chỉ khi bước 4 thành công
```

⚠ Publish **sau** khi save thành công. Gửi mail chào mừng trước mà `SaveChanges` rollback là đã gửi
mail cho một user không tồn tại.
⚠ `ClearDomainEvents()` phải gọi trước khi publish, nếu không handler nào gọi `SaveChanges` lần nữa
sẽ publish lặp vô hạn.

---

# ✓ Nghiệm thu

Chạy cả 2 process: API `:5289` và Gateway `:5100`.

- [ ] `dotnet build` — 0 error, 0 warning
- [ ] `POST http://localhost:5100/api/auth/login` (qua **gateway**) với tài khoản seed → 200 + token
- [ ] Dán token vào jwt.io → thấy đủ `sub`, `email`, `name`, `role`, `exp`
- [ ] Sai mật khẩu → 401, message **giống hệt** khi sai email
- [ ] Email không tồn tại → 401, không phải 404
- [ ] `GET /api/auth/me` không token → 401; có token → 200, **không có** `passwordHash` trong response
- [ ] Body `{ "email": "", "password": "" }` → 400 kèm `errors` (chứng minh ValidationBehavior đã gắn)
- [ ] Log hiện dòng thời gian xử lý của `LoggingBehavior`, **không** thấy mật khẩu trong log
- [ ] Bảng `Users` có unique index trên `Email`
- [ ] User seed có `IsActive = 1`, `LastLoginAt` được cập nhật sau lần đăng nhập đầu

# Ngoài phạm vi — ghi backlog

- Refresh token (entity `RefreshToken`, `POST /api/auth/refresh`, xoay vòng mỗi lần dùng)
- `RegisterCommand` / `ChangePasswordCommand` — `User` đã có sẵn method, chỉ thiếu tầng Application.
  `register` phải `[Authorize(Roles = Roles.Admin)]`, bệnh viện không cho người ngoài tự đăng ký
- Khoá tài khoản sau N lần sai mật khẩu
- `CreatedBy` / `UpdatedBy` — làm cùng module Patients

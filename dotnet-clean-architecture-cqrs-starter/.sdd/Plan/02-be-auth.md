# 02 — Backend: xác thực & phát hành token

**BE là nơi phát hành token.** Gateway chỉ forward header `Authorization` xuống, không kiểm tra gì.
Nghĩa là API vừa **issue** vừa **validate** token.

## Bước 2.1 — Domain: User & Role

**→ Tạo `Domain/Entities/User.cs`** — `BaseEntity`, `IAggregateRoot`

| Property | Kiểu | Ghi chú |
|---|---|---|
| `UserName` | `string` | unique |
| `Email` | `string` | unique |
| `PasswordHash` | `string` | ⚠ không bao giờ lộ ra DTO |
| `FullName` | `string` | |
| `Role` | `string` | 1 user 1 role, đủ dùng |
| `IsActive` | `bool` | khoá tài khoản không xoá |
| `LastLoginAt` | `DateTime?` | |

**→ Tạo `Domain/Constants/HospitalRoles.cs`**

```csharp
public static class HospitalRoles
{
    public const string Admin = "Admin";                 // toàn quyền
    public const string Doctor = "Doctor";               // hồ sơ bệnh án, lịch khám của mình
    public const string Nurse = "Nurse";                 // đọc hồ sơ, cập nhật sinh hiệu
    public const string Receptionist = "Receptionist";   // tiếp nhận bệnh nhân, đặt lịch
    public const string Accountant = "Accountant";       // hoá đơn, thanh toán
}
```

⚠ Dùng `const string`, không dùng `enum` — `[Authorize(Roles = ...)]` chỉ nhận string.

**→ Tạo `Domain/Interfaces/IUserRepository.cs`**

```csharp
Task<User?> GetByUserNameAsync(string userName, CancellationToken ct = default);
Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default);
Task<bool> UserNameExistsAsync(string userName, CancellationToken ct = default);
Task AddAsync(User user, CancellationToken ct = default);
Task UpdateAsync(User user, CancellationToken ct = default);
```

## Bước 2.2 — Application: hợp đồng cho tầng ngoài

**→ Tạo `Common/Interfaces/IPasswordHasher.cs`**

```csharp
string Hash(string password);
bool Verify(string password, string passwordHash);
```

**→ Tạo `Common/Interfaces/ITokenService.cs`**

```csharp
AccessToken CreateAccessToken(User user);
```

**→ Tạo `Common/Models/AccessToken.cs`** — record `(string Token, DateTime ExpiresAtUtc)`

⚠ Application chỉ khai báo interface. Cài đặt thật nằm ở Infrastructure (bước 2.4) — vì nó cần
package crypto, mà Application phải giữ sạch.

## Bước 2.3 — Application: use-case Auth

Folder `Application/Auth/`:

```
Auth/
├── Commands/
│   ├── Login/            LoginCommand.cs + LoginCommandHandler.cs
│   ├── Register/         RegisterCommand.cs + RegisterCommandHandler.cs
│   └── ChangePassword/   ChangePasswordCommand.cs + ChangePasswordCommandHandler.cs
├── Queries/
│   └── GetCurrentUser/   GetCurrentUserQuery.cs + GetCurrentUserQueryHandler.cs
├── Validators/
│   ├── LoginCommandValidator.cs
│   ├── RegisterCommandValidator.cs
│   └── ChangePasswordCommandValidator.cs
└── Models/
    ├── LoginResult.cs    (Token, ExpiresAtUtc, User)
    └── UserDto.cs        (Id, UserName, Email, FullName, Role) — KHÔNG có PasswordHash
```

**`LoginCommandHandler` — logic:**
1. `GetByUserNameAsync` → không thấy thì ném `UnauthorizedAccessException`.
2. `IsActive == false` → ném `UnauthorizedAccessException`.
3. `_hasher.Verify(...)` sai → ném `UnauthorizedAccessException`.
4. Set `LastLoginAt = DateTime.UtcNow`, `UpdateAsync`.
5. `_tokenService.CreateAccessToken(user)` → trả `LoginResult`.

⚠ **Ba trường hợp 1–3 phải trả về cùng một message** ("Tên đăng nhập hoặc mật khẩu không đúng").
Phân biệt "sai user" với "sai mật khẩu" là lỗ hổng dò tài khoản.

**`RegisterCommandValidator`:** mật khẩu ≥ 8 ký tự, có chữ hoa + chữ thường + số.
`UserName` 3–50 ký tự, chỉ chữ/số/dấu chấm/gạch dưới.

## Bước 2.4 — Infrastructure: cài đặt thật

**→ Tạo `Repositories/UserRepository.cs`** — implement `IUserRepository`.
⚠ Query đọc dùng `AsNoTracking()`, query để update thì **không** dùng.

**→ Tạo `Security/PasswordHasher.cs`**
Bọc `Microsoft.AspNetCore.Identity.PasswordHasher<User>` (PBKDF2, đã có salt riêng mỗi bản ghi).
⚠ Tuyệt đối không tự viết hash bằng `SHA256(password)`.

**→ Tạo `Security/JwtOptions.cs`** — bind section `Jwt`:

```csharp
public string Issuer { get; set; } = string.Empty;
public string Audience { get; set; } = string.Empty;
public string SigningKey { get; set; } = string.Empty;   // >= 32 ký tự cho HS256
public int AccessTokenMinutes { get; set; } = 60;
```

**→ Tạo `Security/JwtTokenService.cs`** — implement `ITokenService`.
Claims bắt buộc phải có:

| Claim | Giá trị |
|---|---|
| `sub` | `user.Id` |
| `name` | `user.UserName` |
| `role` | `user.Role` |
| `email` | `user.Email` |
| `jti` | `Guid.NewGuid()` |
| `iss` / `aud` / `exp` / `iat` | từ `JwtOptions` |

**→ Sửa `InfrastructureServiceExtensions.cs`** — đăng ký `IUserRepository`, `IPasswordHasher`,
`ITokenService`, và `services.Configure<JwtOptions>(configuration.GetSection("Jwt"))`.

⚠ Chữ ký hiện tại là `AddInfrastructureServices(this IServiceCollection, string? connectionString)`.
Đổi thành nhận `IConfiguration` để bind được `JwtOptions`, rồi sửa lời gọi trong `Program.cs`.

## Bước 2.5 — API: bật validate JWT

**→ Sửa `appsettings.json`**

```json
"Jwt": {
  "Issuer": "https://localhost:7163",
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

Thêm đoạn fail-fast lúc khởi động nếu key rỗng — thà chết lúc start còn hơn chạy với key rỗng.

**→ Sửa `Program.cs`** — thêm `AddAuthentication().AddJwtBearer(...)` với
`TokenValidationParameters`: validate issuer / audience / lifetime / signing key, `ClockSkew` 30 giây.
⚠ Đặt `options.MapInboundClaims = false` để claim giữ nguyên tên `sub` / `role`, không bị .NET đổi thành URI dài.

**→ Tạo `Controllers/AuthController.cs`** — `[Route("api/auth")]`

| Method | Route | `[Authorize]`? | Trả về |
|---|---|---|---|
| POST | `login` | `[AllowAnonymous]` | `LoginResult` |
| POST | `register` | `[Authorize(Roles = Admin)]` | 201 |
| POST | `change-password` | `[Authorize]` | 204 |
| GET | `me` | `[Authorize]` | `UserDto` |

⚠ `register` **không** để anonymous — bệnh viện không cho người ngoài tự đăng ký.
Tài khoản Admin đầu tiên tạo bằng data seeding (bước 2.6).

Đặt `[Authorize]` ở cấp class, chỉ mở `[AllowAnonymous]` cho `login`. An toàn hơn là ngược lại.

## Bước 2.6 — Seed tài khoản Admin đầu tiên

**→ Tạo `Infrastructure/Persistence/DbInitializer.cs`**
Chạy lúc khởi động: `Database.MigrateAsync()`, nếu chưa có user nào thì tạo 1 Admin,
mật khẩu lấy từ config `Seed:AdminPassword` (user-secrets).
⚠ Không hardcode mật khẩu admin trong source.

## F. Refresh token — chưa làm ở giai đoạn này

Access token 60 phút, hết hạn thì FE bắt user đăng nhập lại. Khi cần làm:
tạo entity `RefreshToken` (Token, UserId, ExpiresAt, RevokedAt), endpoint `POST /api/auth/refresh`,
xoay vòng token mỗi lần dùng. Ghi vào backlog, đừng làm chung phase này.

✓ **Xong phase 2 khi:**
- `POST /api/auth/login` qua **gateway `:5100`** trả token.
- Dán token vào jwt.io thấy đủ `sub`, `name`, `role`.
- `GET /api/auth/me` không kèm token → 401; kèm token → 200.
- Sai mật khẩu và sai username trả **cùng** một message.

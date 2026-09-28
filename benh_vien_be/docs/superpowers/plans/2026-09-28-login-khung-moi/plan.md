# Plan — Đăng nhập đầu-cuối trên khung mới (client → Gateway → API → client)

- **Ngày:** 2026-09-28 (bản 2 — mở rộng từ "chỉ `POST /login`" sang trọn vòng tới khi FE vào trạng thái đã đăng nhập)
- **Căn cứ:** spec `../2026-09-23-auth-permission-redesign/spec.md` §1.1, §1.2, §3.1, §3.2, §3.6, §3.10, §4.3, §4.4, §5, §6.1, §6.4;
  `ARCHITECTURE.md` §1–§3, §5, §9; `.claude/rules/{application,persistence,infrastructure,presentation,api,gateway,frontend,tests}.md`.
- **Mã nghiệp vụ:** NV-03 (đăng nhập). Mã `AT-xx` trong `Dac_ta_nghiep_vu_v2.0.docx` **chưa tra được** lúc lập plan → nghiệm thu
  bám spec §3.2/§9. Không có `OPEN-xx` nào ảnh hưởng.
- **Người viết code:** bạn. Code trong plan là **khung ký hiệu** (chữ ký, bảng, luồng) — không phải bản hoàn chỉnh để chép.

---

## 0. Phạm vi

### 0.1 "Thành công" nghĩa là gì

Người dùng mở `http://localhost:9000/login`, nhập email/mật khẩu đúng → trình duyệt có cookie `__Host-rt` + `__Host-csrf`,
access token nằm trong RAM, `state.auth.status === 'authenticated'` với `user`/`permissions` từ `/me`, và màn hình chuyển trang:
`mustChangePassword` ⇒ `/change-password` (trường hợp admin vừa seed), ngược lại ⇒ trang trước đó hoặc `/`.

Để tới được đó, FE gọi **hai** request, nên cả hai phải chạy trên khung mới:

| # | Request | Gateway | API |
|---|---|---|---|
| R1 | `POST /api/v1/auth/login` | route public, rate limit IP | `LoginCommand` |
| R2 | `GET /api/v1/auth/me` (Bearer) | validate JWT + phiên: Redis `session:{fid}` → **miss/lỗi ⇒ `POST /internal/sessions/validate`** | `GetMeQuery` |
| R2' | `POST /internal/sessions/validate` (chỉ Gateway → API) | — | `ValidateSessionQuery` |

⚠ Vì sao R2' nằm trong phạm vi: `Gateway/Auth/SessionValidator.cs` coi **mọi** phản hồi không thành công của endpoint nội bộ là
`Unavailable` ⇒ **503 `dependency_unavailable`**. Thiếu R2' thì chỉ cần Redis không có key (Redis tắt, restart, hoặc login bỏ qua ghi cache
theo Q3) là `/me` hỏng và FE không vào được.

### 0.2 Ngoài phạm vi (slice sau, thứ tự đề xuất)

`POST /auth/refresh` (F5 giữ đăng nhập, tự gia hạn token) → `logout`/`logout-all` (+ `ICacheInvalidator` + worker) → `change-password` →
`[HasPermission]` + chặn `MustChangePassword` → API quản trị → `/health` kiểm tra DB.

Hệ quả tạm thời, **không phải lỗi của slice này**:
- F5 / mở lại tab: `bootAuth` gọi `/auth/refresh` (còn stub → 500) ⇒ FE về `anonymous` ⇒ phải đăng nhập lại.
- Access token hết hạn sau 15 phút ⇒ request kế tiếp 401 → FE thử refresh → hỏng ⇒ báo "Phiên đăng nhập đã hết hiệu lực".
- Admin seed bị đưa tới `/change-password` nhưng nút đổi mật khẩu chưa chạy.

### 0.3 Hiện trạng (đã đọc 2026-09-28)

| Chặng | Tình trạng |
|---|---|
| FE `benh_vien_fe` | **Đã đúng hợp đồng**: `Login.js` → thunk `login` → `authClient.login` (axios trần, `withCredentials`) → `setAccessToken` → `loadMe` qua `service/http.js` (gắn Bearer) → `AUTH_AUTHENTICATED` → `Redirect`. Lỗi đọc `title` và `Retry-After` từ Problem Details. Dev proxy `/api` → `http://localhost:5100` (`config/webpackDevServer.config.js`) nên cùng origin. Test Jest cho luồng login **chưa có** (chỉ có `permissions.test.js`) |
| Gateway | **Không cần sửa code**: route `auth-login-route` (public), `auth-route` (cần token), JWT HS256, `SessionValidator`, rate limit IP 10/phút cho login |
| Domain | Dùng được: `User`, `SessionFamily.Start`, `ISessionRepository`, `IUserRepository`, `IUnitOfWork`, `AuditRecord` |
| Application | Stub: `Result`, `Error`, `AuthErrors`, mọi port, `MeDto`, `GetMeQueryHandler`, `LoginCommandHandler`, `DependencyInjection`. `ValidationBehavior` sai namespace (`KhoHoSo…`) và đang `public` |
| Persistence | Toàn bộ class rỗng; `Migrations/` trống |
| Infrastructure | Adapter cũ còn nguyên nhưng implement port **cũ đã bị xóa**; EF cũ + migration `20260923142515_InitialIdentityAccess` (DB dev đã áp dụng) ở `Infrastructure/Persistence/`; `IdentityReadService`/`SessionValidationService` cũ đọc DB trực tiếp từ Infrastructure (trái khung mới) |
| Presentation | `AuthEndpoints` khai route, handler stub; chưa có map `Result` → HTTP, ghi cookie, endpoint `/internal` |
| API | `Program.cs` bản cũ tham chiếu namespace đã xóa → **solution hiện không build**. csproj chưa tham chiếu Persistence, Presentation |
| tests | Viết cho khung cũ. `LoginTests` (6), `MeAndSessionsTests`, `InternalSessionValidationTests` mô tả đúng hợp đồng |

Bản cũ của các file API bị xóa (middleware, exception handler, `CurrentUser`, `HttpRequestContext`, `SensitiveDataDestructuringPolicy`,
`AuthCookieWriter`, controller internal) còn trong `HEAD` dưới `dotnet-clean-architecture-cqrs-starter/` (đang hiện ` D` trong
`git status`). Tìm bằng `git ls-tree -r --name-only HEAD | grep -E "API/"` rồi `git show HEAD:<đường-dẫn>` để port.

---

## 1. Quyết định (đã chốt 2026-09-28 — cả 6 mục theo đề xuất)

| # | Vấn đề | Quyết định | Trạng thái |
|---|---|---|---|
| Q1 | Migration cũ ở `Infrastructure/Persistence/Migrations/`, DB dev đã áp dụng | **Chuyển 3 file** sang `Persistence/Migrations/`, chỉ đổi `namespace`/`using` để trỏ `QuanLyBenhVien.Persistence.AppDbContext`, **giữ nguyên** `[Migration("20260923142515_InitialIdentityAccess")]`. Không tạo migration khởi đầu mới (đổi lịch sử). Là sửa file đã commit nhưng chỉ đổi namespace, không đổi schema — đúng trường hợp `ARCHITECTURE.md` §9 dự liệu | Đã chốt |
| Q2 | Test project không build vì còn test của slice chưa chuyển | Tạm loại bằng `<Compile Remove="…" />` **liệt kê từng file** + comment slice đích; báo `NOT_RUN`; gỡ dần. Không xóa test | Đã chốt |
| Q3 | Spec §4.3 nói login ghi `session:{fid}` "không có race". Thực tế có: login commit → khóa tài khoản chạy xen (revoke family, xóa key, tăng `:gen`) → login mới ghi `session:{fid}` với `sv` cũ ⇒ Gateway coi phiên còn sống tới 7 ngày | Login ghi cache **có điều kiện** "`session:{fid}:gen` chưa tồn tại" — cùng một method ghi-có-điều-kiện với R2' (§4 bước A1). Ghi chỗ lệch vào spec §4.3 | Đã chốt — cần sửa spec §4.3 |
| Q4 | `User.RecordLogin()` gọi `DateTimeOffset.UtcNow` trong entity, trái `rules/domain.md` | Đổi thành `RecordLogin(DateTimeOffset now)`; `Touch()` để nguyên | Đã chốt |
| Q5 | Entity `CacheInvalidation` ở Infrastructure; Persistence phải map bảng `CacheInvalidations` (thiếu ⇒ migration sau sinh `DropTable`) | Chuyển entity sang `Persistence/Caching/CacheInvalidation.cs` | Đã chốt |
| Q6 | Đọc DB cho R2' và `/me` đang ở Infrastructure (`SessionValidationService`, `IdentityReadService`) | Tách: đọc DB → read service ở Persistence; điều phối (đọc thế hệ cache → đọc DB → quyết định → ghi cache) → handler Application; Redis → adapter Infrastructure | Đã chốt |

---

## 2. Luồng đầu-cuối

```
[Browser :9000]  Login.js handleSubmit
   │  thunk login(email, password)                                   feature/Auth/redux/actions.js
   │  authClient.login → axios trần POST /api/v1/auth/login           feature/Auth/api/authClient.js
   ▼  (webpack dev proxy /api → :5100, cùng origin)
[Gateway :5100]  CorrelationId → IpRateLimit (10/phút/IP) → route auth-login-route (anonymous) → YARP
   ▼
[API :5289]  ForwardedHeaders → CorrelationId → ExceptionHandler → Serilog → AuthN → UserLogContext → AuthZ
   │  AuthEndpoints.LoginAsync → ISender.Send(LoginCommand)
   │  LoggingBehavior → ValidationBehavior (400) → LoginCommandHandler (§3.1)
   │  ◄ Result<AuthTokensResult>
   │  Thất bại → Error.ToProblem() → 401 unauthenticated | 429 rate_limited + Retry-After
   │  Thành công → AuthCookieWriter.Write (Set-Cookie __Host-rt, __Host-csrf) → 200 AccessTokenDto
   ▼
[Browser]  setAccessToken(accessToken, expiresAtUtc)  (RAM)          feature/Auth/session/tokenStore.js
   │  dispatch(loadMe()) → http.get('v1/auth/me') + Authorization: Bearer   service/http.js
   ▼
[Gateway]  JwtBearer (HS256, iss, aud, exp, ClockSkew 30s) → OnTokenValidated:
   │  Redis session:{fid} có ⇒ so sv + absExp            ── bình thường: login vừa ghi key
   │  miss/lỗi ⇒ POST {API}/internal/sessions/validate + X-Internal-Key  ── R2'
   │  hợp lệ → route auth-route → YARP
   ▼
[API]  JwtBearer validate lại (không tra phiên) → fallback policy (đã đăng nhập) → AuthEndpoints.MeAsync
   │  GetMeQuery → GetMeQueryHandler(ICurrentUser.UserId) → IAuthReadService.GetMeAsync → 200 MeDto
   ▼
[Browser]  AUTH_AUTHENTICATED { user, permissions, mustChangePassword }   feature/Auth/redux/reducer.js
           Login.render: status authenticated ⇒ <Redirect to={mustChangePassword ? '/change-password' : from}>
```

---

## 3. Thiết kế từng use case BE

### 3.1 `LoginCommand` (R1) — Command, transaction kiểu "một `SaveChangesAsync`"

```
email = User.NormalizeEmail(cmd.Email)
lockout = limiter.GetLockoutRemainingAsync(email)                ── Redis, ngoài transaction
lockout? → audit(RateLimited, Denied, "EmailLockout") → SaveChanges → return AuthErrors.TooManyAttempts(lockout)
user = users.GetByEmailAsync(email)                              ── tracking
reason = CheckCredentials(user, password)    null user ⇒ SimulateVerify + "EmailNotFound"; sai pass ⇒ "InvalidPassword";
                                             đúng pass nhưng !IsActive ⇒ "AccountInactive"
reason != null → limiter.RegisterFailureAsync → audit(Login, Failed, reason, actor=user?.Id)
               → SaveChanges (commit TRƯỚC khi trả lỗi) → return AuthErrors.InvalidCredentials
now = time.GetUtcNow(); refresh = refreshTokens.Generate()
family = SessionFamily.Start(user.Id, refresh.Hash, now, ctx.IpAddress, ctx.UserAgent)
user.RecordLogin(now); sessions.AddAsync(family); audit(Login, Succeeded, "SessionFamily", family.Id, actor=user.Id)
SaveChanges                                                      ── MỘT lần; EF tự bọc transaction
── sau commit ──
limiter.ResetAsync(email)
sessionCache.SetIfGenerationUnchangedAsync(entry, CacheGeneration.None)     ── Q3; lỗi Redis: adapter nuốt + Warning
access = accessTokens.Issue(user.Id, family.Id, user.SecurityVersion); csrfToken = csrf.Create(family.Id)
return AuthTokensResult(access.Value, access.ExpiresAtUtc, refresh.Token, family.AbsoluteExpiresAtUtc, csrfToken, user.MustChangePassword)
```

⚠ Kiểm mật khẩu **trước** `IsActive`. Nhánh email không tồn tại **phải** `SimulateVerify`. `ResetAsync` đặt **sau** commit.
Không idempotency-key (login không thuộc §11.1). Không log mật khẩu/token/hash.

### 3.2 `ValidateSessionQuery` (R2') — Query, không ghi DB, có nạp lại cache

```
generation = sessionCache.ReadGenerationAsync(fid)               ── TRƯỚC khi đọc DB; null = Redis lỗi
state = authRead.GetSessionStateAsync(fid)                       ── AsNoTracking, join Users
valid = state != null && Status == Active && AbsoluteExpiresAtUtc > now && IsActive && SecurityVersion == sv
!valid → return SessionValidationDto(false, null, null)          ── 200 { valid: false }, không ghi cache
generation != null → sessionCache.SetIfGenerationUnchangedAsync(entry, generation)
return SessionValidationDto(true, state.UserId, state.AbsoluteExpiresAtUtc)
```

Luôn trả `Result` thành công (hợp lệ hay không đều 200 — spec §3.10). Sai/thiếu `X-Internal-Key` ⇒ 401 ở filter, không vào handler.

### 3.3 `GetMeQuery` (R2) — Query

`userId = currentUser.UserId` (null ⇒ `AuthErrors.Unauthenticated`, thực tế không xảy ra vì route bắt đăng nhập) →
`authRead.GetMeAsync(userId)` (null ⇒ `AuthErrors.Unauthenticated`) → `Result<MeDto>`.
`permissions` = (∪ quyền của mọi role) ∪ quyền lẻ; rỗng nếu `!IsActive`; sắp xếp ordinal. `/me` không chứa PHI ⇒ không `IAuditedRequest`.

---

## 4. Các bước

Thứ tự khuyến nghị: **A → B → C → D → E → F → G**. Mỗi bước có "Xong khi".

### A. Nền BE

**A1 — Application `Common/`**

| File | Nội dung |
|---|---|
| `Common/Results/ErrorType.cs` (mới) | `enum ErrorType { Validation, Unauthorized, Forbidden, NotFound, Conflict, Precondition, TooManyRequests }` |
| `Common/Results/Error.cs` | `sealed record Error(string Code, string Message, ErrorType Type) { public TimeSpan? RetryAfter { get; init; } }` |
| `Common/Results/Result.cs` | `Result`: `IsSuccess`, `IsFailure`, `Error? Error`, `static Success()`, `static Failure(Error)`, ctor `protected` giữ bất biến (thành công ⇔ `Error is null`), `implicit operator Result(Error)`. `Result<TValue>`: `TValue Value` (ném `InvalidOperationException` khi thất bại), `implicit` từ `TValue` và từ `Error` |
| `Behaviors/ValidationBehavior.cs` | Sửa namespace → `QuanLyBenhVien.Application.Behaviors`, `internal sealed` |
| `Common/Auditing/AuditActions.cs` (mới) | `Login = "auth.login"`, `RateLimited = "auth.rate_limited"` (namespace `…Common.Auditing` — test đang dùng) |
| `Common/Auditing/IAuditWriter.cs` | `void Record(string action, AuditResult result, string? reason = null, string? resourceType = null, string? resourceId = null, Guid? actorId = null, IReadOnlyDictionary<string, object?>? metadata = null)` — chỉ **thêm** vào unit of work |
| `Common/Identity/IRequestContext.cs` | `string? CorrelationId`, `string? IpAddress`, `string? UserAgent` |
| `Common/Identity/ICurrentUser.cs` | `Guid? UserId`, `Guid? SessionFamilyId` (khớp `UnitTests/API/Services/CurrentUserTests.cs` cũ) |
| `Common/Security/IPasswordHasher.cs` | `string Hash(string)`, `bool Verify(string password, string passwordHash)`, `void SimulateVerify(string password)` |
| `Common/Caching/SessionCacheEntry.cs` (mới) | `sealed record SessionCacheEntry(Guid SessionFamilyId, Guid UserId, int SecurityVersion, DateTimeOffset AbsoluteExpiresAtUtc)` |
| `Common/Caching/CacheGeneration.cs` (mới) | `sealed record CacheGeneration(string? Value) { public static readonly CacheGeneration None = new((string?)null); }` — giá trị mờ, Application không biết Redis |
| `Common/Caching/ISessionCache.cs` | `Task<CacheGeneration?> ReadGenerationAsync(Guid sessionFamilyId, CancellationToken ct = default)` (null = Redis lỗi); `Task SetIfGenerationUnchangedAsync(SessionCacheEntry entry, CacheGeneration expected, CancellationToken ct = default)` (không ném khi Redis lỗi) |
| `DependencyInjection.cs` | `AddApplication(this IServiceCollection)`: MediatR từ assembly + behavior Logging → Validation; `AddValidatorsFromAssembly(…, includeInternalTypes: true)` |

**A2 — Persistence**

| File | Nội dung |
|---|---|
| `AppDbContext.cs` | `public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IUnitOfWork` — DbSet `Users, Roles, Permissions, SessionFamilies, RefreshTokens, AuditLogs, AuditRecords, CacheInvalidations`; `BeginTransactionAsync`; `ApplyConfigurationsFromAssembly` |
| `AppDbTransaction.cs` | Port nguyên |
| `Configurations/Identity/*.cs` (8), `Configurations/Common/*.cs` (3) | Port **nguyên văn** từ `Infrastructure/Persistence/Configurations/`, chỉ đổi namespace, `internal sealed`. Không đổi tên bảng/cột/độ dài/index |
| `Caching/CacheInvalidation.cs` (Q5) | Chuyển entity, giữ thành viên |
| `Interceptors/AuditSaveChangesInterceptor.cs` | Port; lấy `CorrelationId`/`UserId` qua `IRequestContext`/`ICurrentUser` |
| `Repositories/Identity/{User,Session,Role}Repository.cs` | Port nguyên |
| `Repositories/Common/AuditWriter.cs` (mới, thay `AuditRecorder`) | `internal sealed class AuditWriter(AppDbContext db, ICurrentUser currentUser, IRequestContext request, TimeProvider time) : IAuditWriter`, giữ giới hạn metadata 4 KB |
| `Seed/{SeedOptions,IdentitySeeder,DbInitializer}.cs` | Port; `DbInitializer` đọc `Database:MigrateOnStartup` |
| `DesignTimeDbContextFactory.cs` | Cho `dotnet ef` |
| `Migrations/` (Q1) | Chuyển 3 file, sửa `namespace QuanLyBenhVien.Persistence.Migrations` + `using QuanLyBenhVien.Persistence;`, giữ `[Migration(...)]` |
| `DependencyInjection.cs` | `AddPersistence(this IServiceCollection, IConfiguration)`: DbContext (Npgsql + interceptor), `IUnitOfWork` → cùng `AppDbContext`, 3 repository, `IAuditWriter`, `IAuthReadService` (bước C/D), seeder; `public static Task InitializeDatabaseAsync(this IServiceProvider)` |

⚠ Có thể cần package `Microsoft.Extensions.Options.ConfigurationExtensions`.

**A3 — Infrastructure** (chỉ đổi port, không đổi thuật toán)

| File | Việc |
|---|---|
| `Security/JwtTokenService.cs` | `: IAccessTokenIssuer`, `Issue(...)`; inject `TimeProvider` thay `DateTimeOffset.UtcNow` |
| `Security/RefreshTokenGenerator.cs`, `Security/CsrfTokenService.cs`, `Security/PasswordHasher.cs` | Implement port mới |
| `Caching/LoginRateLimiter.cs` | `: ILoginAttemptLimiter` |
| `Caching/SessionCache.cs` | `: ISessionCache`. `ReadGenerationAsync` → `GuardedCacheWrite.ReadGenerationAsync` (bắt `RedisFailure` ⇒ null). `SetIfGenerationUnchangedAsync` → `GuardedCacheWrite.SetIfUnchangedAsync(db, CacheKeys.Session(fid), SessionCachePayload.Serialize(entry), expected.Value is null ? RedisValue.Null : expected.Value, ttl)`; TTL = `AbsoluteExpiresAtUtc − now`, ≤ 0 thì bỏ |
| `DependencyInjection.cs` (mới, thay `DependencyInjection/InfrastructureServiceExtensions.cs`) | `AddInfrastructure(this IServiceCollection, IConfiguration)`: options `Jwt`/`Auth` + `ValidateOnStart` (giữ kiểm khóa ≥ 32 ký tự, lọc `AllowedOrigins` rỗng), `IConnectionMultiplexer` singleton (`AbortOnConnectFail=false`, timeout như cũ), `TimeProvider.System`, 6 adapter, `RedisHealthCheck` (`Degraded`) |
| Mã cũ dựa trên `Infrastructure/Persistence/AppDbContext` | Xóa phần đã port (AppDbContext, configuration, repository, seed, migration, `AuditRecorder`, `IdentityReadService`, `SessionValidationService`, `EffectivePermissionsQuery`). Phần của slice sau (`PermissionService`, `PermissionCachePayload`, `CacheInvalidator/Processor/Worker`, `DatabaseHealthCheck`) — `<Compile Remove>` kèm comment, hoặc xóa rồi lấy lại từ git. Chọn một, ghi rõ |

**A4 — Presentation (thành phần HTTP dùng chung)**

| File | Nội dung |
|---|---|
| `Http/ProblemResponses.cs` (mới) | `public static IResult Create(HttpContext http, int status, string code, string title, IDictionary<string, string[]>? errors = null)` → `{ type, title, status, code, traceId, errors? }`, `traceId` = correlation id. **Dùng chung** với exception handler của API |
| `Http/ResultExtensions.cs` (mới) | `public static IResult ToProblem(this Error error, HttpContext http)`: `ErrorType` → 400/401/403/404/409/412/429; `RetryAfter` ⇒ `Retry-After` = số giây làm tròn lên, tối thiểu 1 |
| `DependencyInjection.cs` (mới) | `AddPresentation(this IServiceCollection, IConfiguration)`: `AddCarter()`, `AuthCookieWriter`, options cho filter nội bộ (C2) |

**A5 — API (host)**

| File | Nội dung |
|---|---|
| `QuanLyBenhVien.API.csproj` | Thêm `ProjectReference` Persistence, Presentation |
| `Security/JwtAuthenticationSetup.cs` | `AddApiSecurity(IServiceCollection, IConfiguration)`: JwtBearer HS256, validate alg/iss/aud/lifetime/chữ ký, `ClockSkew = 30s`, `MapInboundClaims = false`; `FallbackPolicy = RequireAuthenticatedUser`. Khóa đọc cùng section `Jwt` với Infrastructure |
| `Security/CurrentUser.cs`, `Security/HttpRequestContext.cs` | Port từ `API/Services/` cũ; `UserId` từ claim `sub`, `SessionFamilyId` từ `fid` |
| `Security/SensitiveDataDestructuringPolicy.cs` | Port nguyên |
| `Middleware/CorrelationIdMiddleware.cs`, `Middleware/UserLogContextMiddleware.cs` | Port nguyên |
| `Middleware/GlobalExceptionHandler.cs` | `IExceptionHandler` dùng `ProblemResponses.Create`: `ValidationException` → 400 `validation_failed`, khóa `errors` **camelCase** (`Email` → `email`); `BadHttpRequestException` (JSON hỏng/thiếu body) → 400 `validation_failed`; exception miền theo bản cũ; còn lại 500, không lộ chi tiết. Lỗi 401/403 do policy cũng ra Problem Details |
| `Composition/DependencyInjection.cs` | `AddApiServices(this WebApplicationBuilder)`: gọi `AddApplication`, `AddPersistence`, `AddInfrastructure`, `AddPresentation`, `AddApiSecurity`; `ICurrentUser`/`IRequestContext` scoped; `AddHttpContextAccessor`; exception handler; `ConfigureHttpJsonOptions` (camelCase, bỏ null); ForwardedHeaders; Swagger |
| `Program.cs` | Viết lại theo thứ tự `rules/api.md`: ForwardedHeaders → CorrelationId → ExceptionHandler → Serilog request logging → Authentication → UserLogContext → Authorization → `MapCarter()` → `MapHealthChecks("/health").AllowAnonymous()`. `InitializeDatabaseAsync()` trước `RunAsync`. Giữ `public partial class Program` |

⚠ `run.ps1` chạy API bằng profile `http` (chỉ `:5289`) nên `UseHttpsRedirection` không chuyển hướng request từ Gateway. Nếu giữ
middleware này, đừng đổi sang profile `https` khi chạy sau Gateway.

**Xong khi (A):** `dotnet build benh_vien_be.sln` (các project `src/`) 0 lỗi; `dotnet ef migrations has-pending-model-changes --project
src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API` → không thay đổi (nếu báo lệch: sửa configuration, **không**
sửa snapshot); `dotnet ef migrations list` trên DB dev thấy migration `Applied`.

### B. Chặng R1 — `POST /api/v1/auth/login`

| File | Nội dung |
|---|---|
| `Application/Features/Auth/Common/IAccessTokenIssuer.cs` | `AccessToken Issue(Guid userId, Guid sessionFamilyId, int securityVersion)`; tách `AccessToken.cs` (1 file = 1 type) |
| `…/Common/IRefreshTokenGenerator.cs` + `GeneratedRefreshToken.cs` | `GeneratedRefreshToken Generate()`, `string Hash(string token)`; `record GeneratedRefreshToken(string Token, string Hash)` |
| `…/Common/ICsrfTokenService.cs` (mới) | `string Create(Guid sessionFamilyId)`, `bool IsValid(Guid sessionFamilyId, string? token)` |
| `…/Common/ILoginAttemptLimiter.cs` | `Task<TimeSpan?> GetLockoutRemainingAsync(string normalizedEmail, CancellationToken ct = default)`, `RegisterFailureAsync`, `ResetAsync` |
| `…/Common/AuthErrors.cs` | `InvalidCredentials = new("unauthenticated", "Email hoặc mật khẩu không đúng.", ErrorType.Unauthorized)`; `Unauthenticated = new("unauthenticated", "Chưa đăng nhập hoặc phiên đã hết hạn.", ErrorType.Unauthorized)`; `static Error TooManyAttempts(TimeSpan retryAfter) => new("rate_limited", "Bạn đã thử quá nhiều lần. Vui lòng thử lại sau.", ErrorType.TooManyRequests) { RetryAfter = retryAfter }` |
| `…/Common/AccessTokenDto.cs` (mới) | `record AccessTokenDto(string AccessToken, DateTimeOffset ExpiresAtUtc, bool MustChangePassword)` — body trả FE (FE đọc `accessToken`, `expiresAtUtc`). **Không** serialize `AuthTokensResult` vì chứa refresh token |
| `…/Login/LoginCommandValidator.cs` | `Email`: `NotEmpty`, `EmailAddress`, `MaximumLength(256)`; `Password`: `NotEmpty`, `MaximumLength(128)`; message tiếng Việt |
| `…/Login/LoginCommandHandler.cs` | Primary ctor: `IUserRepository, ISessionRepository, IUnitOfWork, IPasswordHasher, IAccessTokenIssuer, IRefreshTokenGenerator, ICsrfTokenService, ILoginAttemptLimiter, ISessionCache, IAuditWriter, IRequestContext, TimeProvider`. Thân theo §3.1; `private string? CheckCredentials(User? user, string password)` |
| `Domain/Identity/User.cs` (Q4) | `RecordLogin(DateTimeOffset now)` |
| `Presentation/Auth/AuthCookieWriter.cs` (mới) | `sealed class AuthCookieWriter(TimeProvider time)`: `Write(HttpResponse, string refreshToken, string csrfToken, DateTimeOffset sessionExpiresAtUtc)`, `Clear(HttpResponse)`; `__Host-rt` HttpOnly, `__Host-csrf` không HttpOnly; cả hai Secure, SameSite=Strict, Path=/, không Domain, Max-Age = còn lại của family |
| `Presentation/Endpoints/V1/Auth/AuthEndpoints.cs` | `LoginAsync(LoginRequest req, HttpContext http, ISender sender, AuthCookieWriter cookies, CancellationToken ct)`: thất bại → `ToProblem`; thành công → `cookies.Write` + `Results.Ok(new AccessTokenDto(...))` |

**Xong khi (B):** API chạy local; `POST http://localhost:5289/api/v1/auth/login` với admin seed → 200 `mustChangePassword: true` + 2 cookie;
`redis-cli GET session:<fid>` có `sv`.

### C. Chặng R2' — `POST /internal/sessions/validate`

| File | Nội dung |
|---|---|
| `Application/Features/Auth/ValidateSession/ValidateSessionQuery.cs` | `record ValidateSessionQuery(Guid SessionFamilyId, int SecurityVersion) : IQuery<Result<SessionValidationDto>>` |
| `…/ValidateSession/SessionValidationDto.cs` | `record SessionValidationDto(bool Valid, Guid? UserId, DateTimeOffset? AbsoluteExpiresAtUtc)` — Gateway chỉ đọc `valid` |
| `…/ValidateSession/ValidateSessionQueryHandler.cs` | `(IAuthReadService authRead, ISessionCache sessionCache, TimeProvider time)`; thân theo §3.2 |
| `…/Common/SessionStateDto.cs` (mới) | `record SessionStateDto(Guid UserId, SessionStatus Status, DateTimeOffset AbsoluteExpiresAtUtc, bool IsActive, int SecurityVersion)` |
| `…/Common/IAuthReadService.cs` | `Task<SessionStateDto?> GetSessionStateAsync(Guid sessionFamilyId, CancellationToken ct = default)` (+ `GetMeAsync` ở D) |
| `Persistence/ReadServices/Identity/AuthReadService.cs` | `internal sealed class AuthReadService(AppDbContext db) : IAuthReadService` — `GetSessionStateAsync`: `SessionFamilies` join `Users`, `AsNoTracking`, projection (port từ `SessionValidationService` cũ, bỏ phần Redis) |
| `Presentation/Endpoints/Internal/Sessions/InternalSessionEndpoints.cs` (mới) | Carter module, `MapGroup("/internal/sessions")`, `MapPost("/validate", …)`, `.AllowAnonymous()` (không có JWT người dùng) + `.AddEndpointFilter<InternalApiKeyFilter>()`, `.ExcludeFromDescription()`, `.WithName("ValidateSessionInternal")`. Request `ValidateSessionRequest(Guid FamilyId, int Sv)` cùng thư mục |
| `Presentation/Http/InternalApiKeyFilter.cs` (mới) | `IEndpointFilter`: so `X-Internal-Key` với `InternalApiOptions.Key` bằng `CryptographicOperations.FixedTimeEquals`; sai/thiếu ⇒ 401 `unauthenticated` |
| `Presentation/Http/InternalApiOptions.cs` (mới) | `Key`; bind từ `Auth:InternalApiKey` trong `AddPresentation` |

⚠ Gateway **không** có route `/internal/*` ra ngoài — giữ nguyên. `Identity:InternalApiKey` (Gateway) phải bằng `Auth:InternalApiKey` (API).

**Xong khi (C):** gọi thẳng API với key đúng + family hợp lệ → `200 { valid: true, … }`; `sv` lệch → `{ valid: false }`; thiếu key → 401.

### D. Chặng R2 — `GET /api/v1/auth/me`

| File | Nội dung |
|---|---|
| `Application/Features/Auth/GetMe/MeDto.cs` | `record MeDto(Guid Id, string Email, string FullName, string? AvatarUrl, IReadOnlyList<RoleRefDto> Roles, IReadOnlyList<string> Permissions, bool MustChangePassword)` — FE tách `permissions`, `mustChangePassword`, phần còn lại là `user` |
| `…/GetMe/RoleRefDto.cs` (mới) | `record RoleRefDto(Guid Id, string Code, string Name)` |
| `…/GetMe/GetMeQuery.cs` | Giữ nguyên |
| `…/GetMe/GetMeQueryHandler.cs` | `(ICurrentUser currentUser, IAuthReadService authRead)`; thân theo §3.3 |
| `…/Common/IAuthReadService.cs` | Thêm `Task<MeDto?> GetMeAsync(Guid userId, CancellationToken ct = default)` |
| `Persistence/ReadServices/Identity/AuthReadService.cs` | `GetMeAsync`: port `IdentityReadService.GetMeAsync` + `LoadRoleRefsAsync` |
| `Persistence/ReadServices/Identity/EffectivePermissions.cs` (mới) | `internal static` — port `EffectivePermissionsQuery.LoadAsync` (một chỗ tính quyền hiệu lực; `PermissionService` slice sau dùng lại) |
| `Presentation/Endpoints/V1/Auth/AuthEndpoints.cs` | `MeAsync(HttpContext http, ISender sender, CancellationToken ct)` → `Results.Ok(dto)` hoặc `ToProblem` |

**Xong khi (D):** `GET http://localhost:5289/api/v1/auth/me` + Bearer từ B → 200 đủ trường; không token → 401 Problem Details.

### E. Gateway — không sửa code, chỉ kiểm cấu hình

| Kiểm | Giá trị phải khớp |
|---|---|
| `Jwt:Issuer`, `Jwt:Audience`, `Jwt:SigningKey` | Gateway = API (user-secrets hai project) |
| `Identity:InternalApiKey` (Gateway) | = `Auth:InternalApiKey` (API) |
| `Identity:InternalBaseUrl` / cluster `identity-cluster` | `http://localhost:5289/` |
| Payload `session:{fid}` | `Gateway/Auth/SessionCachePayload.cs` khớp `Infrastructure/Caching/SessionCachePayload.cs` (`sv`, hạn tuyệt đối Unix) — không đổi |

**Xong khi (E):** qua Gateway `:5100`: login → 200; `/me` với Bearer → 200; xóa tay `session:<fid>` trong Redis rồi gọi lại `/me` → vẫn 200
(đi qua R2'), key được ghi lại.

### F. Frontend — code đã có, chỉ đi lại luồng + bổ sung test

Bạn có thể viết lại từng file dưới đây để luyện, nhưng **hợp đồng không đổi** nên không bắt buộc sửa gì:

| File | Vai trò trong luồng |
|---|---|
| `src/feature/Auth/Login.js` | Form; `handleSubmit` → `this.props.login`; lỗi 401/429 hiện `problemTitle`, khóa nút theo `Retry-After`; `status === 'authenticated'` ⇒ `Redirect` |
| `src/feature/Auth/api/authClient.js` | `login(email, password)` — axios **trần** (`withCredentials`, không interceptor refresh) `POST v1/auth/login` |
| `src/feature/Auth/redux/actions.js` | `login` thunk: `authClient.login` → `setAccessToken(data.accessToken, data.expiresAtUtc)` → `dispatch(loadMe())`; `loadMe`: `http.get('v1/auth/me')` → `AUTH_AUTHENTICATED` |
| `src/feature/Auth/session/tokenStore.js` | Giữ token trong biến module — không `localStorage`/`sessionStorage` |
| `src/service/http.js` | Gắn `Authorization: Bearer`; 401 → refresh một lần (sẽ hỏng tới slice refresh) |
| `src/feature/Auth/redux/reducer.js` | `AUTH_AUTHENTICATED` → `{ status, user, permissions, mustChangePassword }` |
| `src/feature/Auth/problem.js` | Đọc `title`, `errors`, `Retry-After` |
| `config/webpackDevServer.config.js` | Proxy `/api` → `:5100` (cùng origin cho cookie `__Host-`) |

⚠ `process.env.API_URL` phải là `/api` khi `npm start` (`pm2.json` đặt `/api`; `config/env.js` đọc từ môi trường) — thiếu thì
axios gọi đường dẫn tương đối theo trang hiện tại.

Test mới (`src/feature/Auth/__tests__/`, Jest, mock `authClient` và `http`):

| File | Case |
|---|---|
| `actions.test.js` | `login` thành công: gọi `authClient.login`, `getAccessToken()` trả token mới, dispatch `AUTH_AUTHENTICATED` với payload `/me`; `authClient.login` ném 401 ⇒ thunk ném lại, **không** dispatch `AUTH_AUTHENTICATED`, token không bị set |
| `reducer.test.js` | `AUTH_AUTHENTICATED` tách đúng `user`/`permissions`/`mustChangePassword` |

**Xong khi (F):** `tooling/validate.ps1 -Mode Frontend` xanh (ESLint + Jest).

### G. Test BE

**G1 — Unit** (`tests/QuanLyBenhVien.UnitTests/`, `FakeTimeProvider`, fake viết tay cho port)

| File | Case |
|---|---|
| `Application/Features/Auth/Login/LoginCommandValidatorTests.cs` | rỗng cả hai → lỗi `Email` và `Password`; email sai định dạng; > 256; password > 128; hợp lệ |
| `Application/Features/Auth/Login/LoginCommandHandlerTests.cs` | (a) bị chặn → `rate_limited`, `RetryAfter` đúng, audit `RateLimited`, `SaveChanges` 1 lần, **không** tra user; (b) email không tồn tại → `SimulateVerify`, `RegisterFailure`, audit `EmailNotFound` actor null; (c) sai mật khẩu → `InvalidPassword`, actor = userId; (d) khóa + đúng mật khẩu → `AccountInactive`; (e) khóa + sai mật khẩu → `InvalidPassword`; (f) thành công → `SaveChanges` đúng 1 lần, `LastLoginAt == now`, `SessionExpiresAtUtc == now + 7 ngày`, `ResetAsync` và ghi cache (với `CacheGeneration.None`) **sau** `SaveChanges`, CSRF cho `family.Id` |
| `Application/Features/Auth/ValidateSession/ValidateSessionQueryHandlerTests.cs` | hợp lệ + generation có ⇒ `valid: true` và ghi cache với đúng generation đã đọc; `sv` lệch / Revoked / hết hạn / user khóa / không tồn tại ⇒ `valid: false`, không ghi cache; Redis lỗi (generation null) ⇒ vẫn `valid: true`, không ghi cache; generation đọc **trước** read service |
| `Application/Features/Auth/GetMe/GetMeQueryHandlerTests.cs` | có user ⇒ DTO; read service trả null ⇒ `unauthenticated` |
| `Presentation/Http/ResultExtensionsTests.cs` | mỗi `ErrorType` → status; body có `code`, `title`, `traceId`; `RetryAfter` 90.2s → `Retry-After: 91` |
| `Presentation/Http/InternalApiKeyFilterTests.cs` | đúng key → gọi tiếp; sai/thiếu/khác độ dài → 401 |
| `Presentation/Auth/AuthCookieWriterTests.cs` | thuộc tính 2 cookie, Max-Age |
| Test cũ chuyển namespace/port | `Infrastructure/Security/*Tests`, `API/Errors/GlobalExceptionHandlerTests` → `API/Middleware/`, `API/Middleware/CorrelationIdMiddlewareTests`, `API/Services/{CurrentUser,HttpRequestContext}Tests` → `API/Security/`, `API/Logging/SensitiveDataDestructuringPolicyTests`, `Infrastructure/Auditing/AuditRecorderTests` → `Persistence/…/AuditWriterTests`, `Infrastructure/Persistence/*` → `Persistence/*`, `Infrastructure/Repositories/UserRepositoryTests` → `Persistence/Repositories/`, `Domain/Identity/UserTests` (Q4) |

**G2 — Integration** (`tests/QuanLyBenhVien.IntegrationTests/`, Docker)

| File | Việc |
|---|---|
| `Helpers/{TestData,TestDb,TestServices,AuthTestClient}.cs` | Đổi `using` sang `QuanLyBenhVien.Persistence` / port mới |
| `Auth/LoginTests.cs` | Giữ 6 test, chỉ sửa `using`. Thêm `Success_WritesLastLoginAndOneSessionFamily` (đúng 1 family; token lưu là hash ≠ cookie) |
| `Auth/MeAndSessionsTests.cs` | Giữ phần `/me` (sau login → 200 đủ trường; không token → 401); phần `/sessions` loại tạm theo Q2 |
| `Auth/InternalSessionValidationTests.cs` | Chạy lại toàn bộ (thiếu/sai key → 401; hợp lệ → valid + ghi lại cache; `sv` lệch → invalid) |
| `Caching/RedisServicesTests.cs` (phần liên quan) | `LoginRateLimiter` 5 lần → khóa, reset → mở, Redis chết → không khóa; `SetIfGenerationUnchangedAsync(None)` không ghi khi `session:{fid}:gen` tồn tại (Q3) |
| `Gateway/GatewayAuthenticationTests.cs` (phần login/me) | Route public đi qua không token; Redis miss → gọi internal validate |
| `Persistence/{SessionRepository,Seed,Repository}Tests.cs` | Đổi `using` |
| Redis tắt | Login với `ConnectionStrings:Redis` trỏ cổng chết → 200 |

Theo Q2, file/test thuộc slice sau loại tạm và báo `NOT_RUN`.

---

## 5. Tiêu chí nghiệm thu đầu-cuối

| # | Quan sát được | Chứng minh bằng |
|---|---|---|
| AC-1 | Đăng nhập đúng trên UI → chuyển `/change-password` (admin seed) hoặc `/` (user đã đổi mật khẩu); Redux `status: 'authenticated'`, có `user.email`, `permissions` | Thủ công qua `:9000`; Redux DevTools |
| AC-2 | Response login: `200 { accessToken, expiresAtUtc, mustChangePassword }`; `Set-Cookie __Host-rt` (HttpOnly, Secure, SameSite=Strict, Path=/, không Domain, Max-Age), `__Host-csrf` (không HttpOnly) | `LoginTests` + DevTools |
| AC-3 | JWT chỉ có `aud, exp, fid, iat, iss, jti, nbf, sub, sv` | `LoginTests` |
| AC-4 | Sai mật khẩu / email không tồn tại / tài khoản khóa → cùng 401 `unauthenticated`, `title "Email hoặc mật khẩu không đúng."`; UI hiện đúng câu đó | `LoginTests` + thủ công |
| AC-5 | Mỗi thất bại có `AuditRecords` `auth.login` `Failed` với `Reason` đúng, đã commit | `LoginTests` |
| AC-6 | Lần sai thứ 6 → 429 `rate_limited` + `Retry-After`; UI khóa nút tới giờ ghi | `LoginTests` + thủ công |
| AC-7 | Body rỗng → 400 `validation_failed`, `errors.email`, `errors.password` | `LoginTests` |
| AC-8 | `/me` sau login → 200 qua Gateway; xóa `session:{fid}` trong Redis → `/me` vẫn 200 (qua internal validate) và key được ghi lại | `GatewayAuthenticationTests` / `InternalSessionValidationTests` + thủ công |
| AC-9 | Tắt Redis → login + `/me` vẫn chạy (chậm hơn), không 503 | Integration + thủ công `docker stop <container redis hospital-*>` |
| AC-10 | Access token chỉ ở RAM: `localStorage`/`sessionStorage` trống | DevTools |
| AC-11 | Log API/Gateway (Seq) không chứa mật khẩu, token, cookie | Seq, lọc theo CorrelationId |
| AC-12 | `has-pending-model-changes` không thay đổi; DB dev vẫn `Applied` migration cũ | `dotnet ef` |

---

## 6. Kiểm tra

1. `powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Quick` (build + unit BE).
2. `… -Mode Full` (integration, cần Docker).
3. `… -Mode Frontend` (ESLint + Jest).
4. `dotnet ef migrations has-pending-model-changes …` (AC-12).
5. `run.ps1` → đi tay AC-1, AC-4, AC-6, AC-8, AC-9, AC-10, AC-11.
6. Đọc lại diff; `/review-diff login-e2e` (thay đổi xuyên 6 project + FE test).

---

## 7. Rủi ro còn lại

- Q1 sửa namespace trong file migration đã commit: nếu `has-pending-model-changes` báo lệch, nguyên nhân là configuration port chưa khớp.
- Loại tạm test (Q2) che hồi quy của các slice chưa chuyển — cần danh sách rõ và gỡ dần.
- Chưa có refresh ⇒ phiên trên UI chỉ sống tới khi access token hết hạn (15 phút) hoặc tới lần F5 kế tiếp (§0.2).
- Chưa có chặn `MustChangePassword` ở BE (§3.9 spec): user bị bắt đổi mật khẩu vẫn gọi được các endpoint chỉ cần đăng nhập — hiện chỉ có `/me`
  nên chưa lộ gì, nhưng phải làm trước khi thêm endpoint nghiệp vụ.
- `/health` chưa kiểm PostgreSQL tới khi chuyển `DatabaseHealthCheck`.

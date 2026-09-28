# Thiết kế lại Đăng nhập, Phiên & Phân quyền — Implementation Plan (tổng quan)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Hiện thực spec `spec.md`: Gateway xác thực JWT + phiên, BE phát token (access 15' trong RAM, refresh rotation trong cookie HttpOnly), phân quyền nhiều role + quyền lẻ cache Redis, audit truy cập/bảo mật và logging có cấu trúc.

**Architecture:** BE (Clean Architecture/CQRS hiện có) thêm module IdentityAccess: Domain (`User`, `Role`, `SessionFamily`, `AuditRecord`), Application (command/query MediatR), Infrastructure (EF Core Postgres, Redis, bảng `CacheInvalidations` + worker). API bật JwtBearer + policy `[HasPermission]`. Gateway (YARP) validate JWT HS256 + tra `session:{fid}` trong Redis, miss thì hỏi BE qua endpoint nội bộ. FE React giữ access token trong closure, refresh single-flight + Web Locks + BroadcastChannel.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core 10 + Npgsql, StackExchange.Redis, YARP 2.3, MediatR 12, FluentValidation 11, Serilog (+ Seq, CompactJson), xUnit, Testcontainers (PostgreSQL, Redis), `Microsoft.AspNetCore.Mvc.Testing`; React 16 + Redux + axios 0.18 + Jest 23.

**Spec:** [`spec.md`](spec.md) (cùng thư mục). Người thực thi đọc spec trước, plan sau.

---

## Global Constraints

Mọi task trong mọi file giai đoạn đều ngầm bao gồm các ràng buộc dưới đây.

**Kiến trúc & mã nguồn**
- `CleanArchCqrs.Domain` luôn có **0** `<PackageReference>` và **0** `<ProjectReference>`. Kiểm tra bằng `dotnet build src/CleanArchCqrs.Domain` sau mọi task đụng Domain.
- `CleanArchCqrs.Gateway` có **0** `<ProjectReference>` — code dùng chung với API (ProblemResponseWriter, RedisHealthCheck, định dạng key Redis) là **bản sao độc lập**, không phải class chung.
- File mới dùng file-scoped namespace, một file một type, tên file = tên type.
- Method bất đồng bộ: hậu tố `Async`, nhận `CancellationToken ct = default`.
- Khoá chính Guid sinh ở domain bằng `Guid.CreateVersion7()`. **Mọi khoá Guid mới** cấu hình EF `ValueGeneratedNever()` (nếu không, EF coi entity con thêm qua navigation là `Modified` và `SaveChanges` ném `DbUpdateConcurrencyException`).
- Thời gian: `DateTimeOffset` **UTC (offset 0)** — Npgsql từ chối ghi offset khác 0. Application lấy giờ qua `TimeProvider` (đăng ký `TimeProvider.System`), không gọi `DateTimeOffset.UtcNow` trong handler.
- Đọc cấu hình **lười** (trong lambda đăng ký / `IOptions<T>`), không đọc giá trị cấu hình ngay lúc gọi `AddXxx(...)` — để `WebApplicationFactory` ghi đè được trong test.
- Controller không chứa nghiệp vụ; chỉ gọi MediatR và map HTTP (cookie, status).

**Hợp đồng HTTP**
- Route: tiền tố `/api/v1`, kebab-case. Nội bộ: `/internal/...` (Gateway không có route ra ngoài cho `/internal`).
- Lỗi: Problem Details RFC 9457 `{ type, title, status, code, traceId, errors? }`, `traceId` = header `X-Correlation-Id`, key trong `errors` là camelCase.
- Mã lỗi (`code`): `validation_failed` (400) · `unauthenticated` (401) · `forbidden`, `password_change_required`, `csrf_failed` (403) · `not_found` (404) · `email_taken`, `last_admin`, `self_action_forbidden`, `conflict` (409) · `rate_limited` (429, kèm `Retry-After`) · `dependency_unavailable` (503) · `internal_error` (500).
- Message login thất bại (cả 3 nhánh): **`Email hoặc mật khẩu không đúng.`**

**Token, cookie, header**
- JWT HS256, access **15 phút**, `ClockSkew` **30 giây** ở cả Gateway và API, `MapInboundClaims = false`. Claims **đúng và chỉ**: `sub, fid, sv, jti, iat, nbf, exp, iss, aud`.
- Phiên (SessionFamily) hạn tuyệt đối **7 ngày**, không trượt; refresh token mới hết hạn đúng bằng hạn family; **strict reuse**.
- Refresh token: 32 byte random, base64url; DB lưu **SHA-256 hex lowercase** (64 ký tự).
- Cookie `__Host-rt` (HttpOnly) và `__Host-csrf` (không HttpOnly): `Secure`, `SameSite=Strict`, `Path=/`, không `Domain`, `Max-Age` = thời gian còn lại của family.
- Header: `X-CSRF-Token`, `X-Internal-Key`, `X-Correlation-Id`.

**Redis**
- Key: `session:{familyId}`, `perm:{userId}`, `rl:email:{sha256hex(emailChuẩnHoá)}`, `rl:ip:{login|refresh}:{ip}`; bộ đếm thế hệ `session:{familyId}:gen`, `perm:{userId}:gen`. Guid in dạng `D` lowercase (mặc định `ToString()`).
- Giá trị `session:{fid}` (JSON, Gateway đọc): `{"userId":"<guid>","sv":<int>,"absExp":<unix seconds>}`, TTL = hạn tuyệt đối.
- Giá trị `perm:{uid}` (JSON): `{"permissions":["users.read",...],"mustChangePassword":false}`, **không TTL**.
- Chỉ BE ghi/xoá `session:*`, `perm:*`. Mọi lần ghi cache từ dữ liệu DB (trừ lúc login tạo family mới) phải dùng **ghi có điều kiện theo thế hệ** (`GuardedCacheWrite`). Mọi lần xoá key đồng thời tăng `…:gen` (TTL 1 ngày).
- Redis lỗi ⇒ **không** fail request: đọc DB thay thế (BE), hỏi BE (Gateway), bỏ qua rate limit IP (Gateway), log Warning.

**Ngưỡng**
- Rate limit email (BE): **5** lần sai / **15 phút** → 429. Rate limit IP (Gateway): login **10**/phút/IP, refresh **30**/phút/IP.
- Mật khẩu mới: **10–128** ký tự, khác mật khẩu hiện tại, không chứa phần local của email (không phân biệt hoa thường).
- Worker xoá cache: mỗi **5 giây**, lô **100** dòng; log Error khi `Attempts ≥ 10`.
- Metadata AuditRecord ≤ **4096 byte** UTF-8.

**Logging & bảo mật**
- Không bao giờ log: body request; header `Authorization`, `Cookie`, `Set-Cookie`, `X-CSRF-Token`, `X-Internal-Key`; property `Password`, `CurrentPassword`, `NewPassword`, `TemporaryPassword`, `AccessToken`, `RefreshToken`, `PasswordHash`, `TokenHash`.
- Secret (`Jwt:SigningKey`, `Auth:CsrfKey`, `Auth:InternalApiKey`, `Seed:AdminPassword`) không commit: dev dùng `dotnet user-secrets`, production dùng biến môi trường/secret store.

**Kiểm thử**
- Unit test: `tests/CleanArchCqrs.UnitTests` (xUnit, EF InMemory/SQLite chỉ để kiểm mapping/logic).
- Integration test: `tests/CleanArchCqrs.IntegrationTests` trên **PostgreSQL + Redis thật qua Testcontainers** — bắt buộc cho mọi hành vi phụ thuộc SQL/Redis (khoá hàng, unique index, transaction, cache). **Docker Desktop phải đang chạy.** Mỗi `ApiFactory` dùng một database riêng.
- Postgres/Redis image: `postgres:17-alpine`, `redis:7.4-alpine`.

**Quy trình**
- Người dùng tự viết code theo plan; Claude review sau từng task. TDD: viết test đỏ → code → test xanh → commit.
- Mỗi task kết thúc bằng `dotnet build` **0 warning** + test liên quan xanh + một commit `type(scope): mô tả`.

---

## Các giai đoạn

| # | File | Kết quả chạy được khi xong | Phụ thuộc |
|---|---|---|---|
| 1 | [plan-01-nen-tang.md](plan-01-nen-tang.md) | Build 0 warning; docker-compose (Postgres/Redis/Seq); project integration test; `/health`; Problem Details; log JSON + Seq + che dữ liệu nhạy cảm | — |
| 2 | [plan-02-domain-du-lieu.md](plan-02-domain-du-lieu.md) | Domain quyền/phiên/audit mới; EF + migration `InitialIdentityAccess`; repository có khoá hàng; seed 9 role + Admin | 1 |
| 3 | [plan-03-be-xac-thuc-phien.md](plan-03-be-xac-thuc-phien.md) | Login/refresh/logout/logout-all/me/sessions/change-password/internal validate chạy thật trên API | 2 |
| 4 | [plan-04-be-phan-quyen-quan-tri.md](plan-04-be-phan-quyen-quan-tri.md) | `[HasPermission]` + cache quyền; bắt đổi mật khẩu lần đầu; API quản trị user/role/permission | 3 |
| 5 | [plan-05-gateway.md](plan-05-gateway.md) | Gateway validate JWT + phiên, route v1, rate limit IP, health; test end-to-end Gateway→API | 3 (4 cho test quyền) |
| 6 | [plan-06-fe.md](plan-06-fe.md) | FE: token trong RAM, refresh chủ động + 401, đa tab, Redux auth, màn Login/Đổi mật khẩu, `Can` | 5 + nền tảng FE `.sdd/Plan/06-fe-nen-tang.md` bước 6.1–6.4, 6.8 |

Thứ tự khuyến nghị: 1 → 2 → 3 → 4 → 5 → 6. Giai đoạn 5 có thể làm song song với 4 sau khi 3 xong.

## Bản đồ file (tổng hợp)

**Domain** (`src/CleanArchCqrs.Domain/`)
- `Identity/`: `Permissions.cs`, `PermissionDefinition.cs`, `Permission.cs`, `SystemRoles.cs`, `Role.cs`, `RolePermission.cs`, `UserRole.cs`, `UserPermission.cs`, `IRoleRepository.cs`; sửa `User.cs`, `IUserRepository.cs`, `Events/UserRegisteredDomainEvent.cs`; thêm `Events/UserRolesChangedDomainEvent.cs`, `Events/UserPermissionsChangedDomainEvent.cs`
- `Identity/Sessions/`: `SessionFamily.cs`, `RefreshToken.cs`, `SessionStatus.cs`, `SessionRevokeReason.cs`, `RotationResult.cs`, `ISessionRepository.cs`
- `Common/`: sửa `IUnitOfWork.cs`; thêm `IUnitOfWorkTransaction.cs`; `Common/Auditing/`: `AuditRecord.cs`, `AuditResult.cs`; sửa `AuditLog.cs`
- Xoá: `Constants/Roles.cs`, `Identity/UserLoginHistory.cs`, `Identity/IUserLoginHistoryRepository.cs`

**Application** (`src/CleanArchCqrs.Application/`)
- `Common/Exceptions/`: `ErrorCodes.cs`, `UnauthorizedException.cs`, `ForbiddenException.cs`, `ConflictException.cs`, `TooManyRequestsException.cs`; sửa `ValidationException.cs`
- `Common/Interfaces/`: `IRequestContext.cs`, `IRefreshTokenGenerator.cs`, `ICsrfTokenService.cs`, `ISessionCache.cs`, `ILoginRateLimiter.cs`, `ICacheInvalidator.cs`, `IAuditRecorder.cs`, `IAuditedRequest.cs`, `IIdentityReadService.cs`, `ISessionValidationService.cs`, `IPermissionService.cs`; sửa `ICurrentUser.cs`, `ITokenService.cs`, `IPasswordHasher.cs`
- `Common/Models/`: `GeneratedRefreshToken.cs`, `SessionCacheEntry.cs`, `UserAccess.cs`, `PagedResult.cs`
- `Common/Behaviors/AuditBehavior.cs`, `Common/Auditing/AuditActions.cs`, `Common/Security/PasswordPolicy.cs`, `Common/Security/PasswordRuleExtensions.cs`
- `Auth/` (viết lại toàn bộ), `Users/`, `Roles/`, `PermissionCatalog/` (**không** đặt tên `Permissions/` — namespace sẽ che static class `Domain.Identity.Permissions`)

**Infrastructure** (`src/CleanArchCqrs.Infrastructure/`)
- `Caching/`: `CacheKeys.cs`, `RedisFailure.cs`, `GuardedCacheWrite.cs`, `CacheInvalidation.cs`, `SessionCachePayload.cs`, `SessionCache.cs`, `LoginRateLimiter.cs`, `CacheInvalidator.cs`, `CacheInvalidationProcessor.cs`, `CacheInvalidationWorker.cs`
- `Identity/`: `EffectivePermissionsQuery.cs`, `IdentityReadService.cs`, `SessionValidationService.cs`, `PermissionCachePayload.cs`, `PermissionService.cs`
- `Auditing/AuditRecorder.cs`, `HealthChecks/DatabaseHealthCheck.cs`, `HealthChecks/RedisHealthCheck.cs`
- `Security/`: `AuthOptions.cs`, `RefreshTokenGenerator.cs`, `CsrfTokenService.cs`; sửa `JwtOptions.cs`, `JwtTokenService.cs`, `PasswordHasher.cs`
- `Persistence/`: sửa `AppDbContext.cs`; thêm `AppDbTransaction.cs`, `DbInitializer.cs`, `Seed/SeedOptions.cs`, `Seed/IdentitySeeder.cs`, `Migrations/*`; `Configurations/`: thêm 10 file, sửa `UserConfiguration.cs`, `AuditLogConfiguration.cs`
- `Repositories/`: `RoleRepository.cs`, `SessionRepository.cs`; sửa `UserRepository.cs`
- Xoá: `Repositories/UserLoginHistoryRepository.cs`, `Persistence/Configurations/UserLoginHistoryConfiguration.cs`

**API** (`src/CleanArchCqrs.API/`)
- `Errors/`: `ProblemResponseWriter.cs`, `GlobalExceptionHandler.cs`
- `Logging/SensitiveDataDestructuringPolicy.cs`, `Middleware/UserLogContextMiddleware.cs`
- `Services/HttpRequestContext.cs`; sửa `Services/CurrentUser.cs`
- `Auth/`: `AuthCookieWriter.cs`, `CsrfProtectionFilter.cs`, `CsrfProtectedAttribute.cs`, `InternalApiKeyFilter.cs`, `InternalApiKeyAttribute.cs`
- `Authorization/`: `HasPermissionAttribute.cs`, `PermissionRequirement.cs`, `PermissionPolicyProvider.cs`, `PermissionAuthorizationHandler.cs`, `PasswordChangeRequirement.cs`, `PasswordChangeRequirementHandler.cs`, `AllowWhilePasswordChangeRequiredAttribute.cs`, `ProblemAuthorizationResultHandler.cs`
- `DependencyInjection/`: `ApiAuthenticationExtensions.cs`, `ApiAuthorizationExtensions.cs`
- `Contracts/`: `Auth/AccessTokenResponse.cs`, `Internal/ValidateSessionRequest.cs`, `Internal/ValidateSessionResponse.cs`, `Users/SetUserRolesRequest.cs`, `Users/PermissionGrantRequest.cs`, `Roles/RenameRoleRequest.cs`, `Roles/SetRolePermissionsRequest.cs`
- `Controllers/`: `AuthController.cs`, `InternalSessionsController.cs`, `UsersController.cs`, `RolesController.cs`, `PermissionsController.cs`

**Gateway** (`src/CleanArchCqrs.Gateway/`)
- `Auth/`: `GatewayJwtOptions.cs`, `IdentityServiceOptions.cs`, `SessionCheck.cs`, `ISessionValidator.cs`, `SessionValidator.cs`, `SessionCachePayload.cs`, `GatewayAuthExtensions.cs`
- `Errors/ProblemResponseWriter.cs`, `HealthChecks/RedisHealthCheck.cs`
- `Middleware/StripInternalHeadersMiddleware.cs`, `Middleware/IpRateLimitMiddleware.cs`

**Tests**
- `tests/CleanArchCqrs.IntegrationTests/` (mới): `Infrastructure/` (fixture, factory), `Helpers/`, và thư mục test theo tính năng
- `tests/CleanArchCqrs.UnitTests/`: thêm/sửa theo từng task

**FE** (`benh_vien_fe/src/`)
- `feature/Auth/`: `session/tokenStore.js`, `session/csrf.js`, `session/refreshCoordinator.js`, `session/refreshScheduler.js`, `api/authClient.js`, `redux/actionTypes.js`, `redux/actions.js`, `redux/reducer.js`, `permissions.js`, `Can.js`, `PrivateRoute.js`, `Login.js`, `ChangePassword.js`, `index.js`
- `service/http.js`; sửa `reducer.js`, `index.js`

## Nghiệm thu cuối (sau giai đoạn 6)

Chạy theo mục 9 của spec, tay trên môi trường dev:

- [ ] `docker compose up -d` → `dotnet build` 0 warning → `dotnet test` xanh (Docker đang chạy).
- [ ] Chạy API (`:5289`) + Gateway (`:5100`) + FE (`:9000`, proxy `/api` → Gateway).
- [ ] Đăng nhập Admin seed → bị chuyển trang đổi mật khẩu → đổi xong vào app.
- [ ] DevTools → Application: có `__Host-rt` (HttpOnly) và `__Host-csrf`; localStorage/sessionStorage không có token.
- [ ] Decode access token: chỉ `sub, fid, sv, jti, iat, nbf, exp, iss, aud`.
- [ ] F5 → vẫn đăng nhập. Mở 2 tab, để quá 15 phút → cả 2 tab vẫn dùng được, Seq thấy mỗi lần chỉ 1 request `/auth/refresh`.
- [ ] Logout ở tab 1 → tab 2 tự về `/login`; access token cũ gọi qua Gateway → 401.
- [ ] Gỡ permission khỏi role → user đó gọi endpoint tương ứng → 403 ngay request kế tiếp.
- [ ] Gọi thẳng API `:5289` bằng token sửa chữ ký → 401.
- [ ] `docker compose stop redis` → vẫn đăng nhập/dùng được; `/health` API và Gateway trả `Degraded`. `docker compose start redis`.
- [ ] Seq: lọc theo `CorrelationId` thấy log cả `gateway` và `api`; tìm `password`, `accessToken`, `refreshToken` không ra giá trị thật.

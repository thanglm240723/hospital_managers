# Thiết kế lại Đăng nhập, Phiên & Phân quyền — Spec

- **Ngày:** 2026-09-23
- **Trạng thái:** Đã duyệt thiết kế từng phần; cập nhật 2026-09-23 khi lập plan (chặn race cache §4.3, đổi mật khẩu §3.8, nơi ghi `authz.denied`)
- **Plan đi kèm:** `plan.md` (tổng quan) + `plan-01…06-*.md` (cùng thư mục)
- **Thay thế:** `.sdd/Plan/luong-login.md`, `.sdd/Plan/07-fe-auth.md`, các dòng Auth/Phân quyền/Audit đăng nhập trong `.sdd/Plan/00-quyet-dinh-va-quy-uoc.md`
- **Căn cứ:** `Dac_ta_nghiep_vu_v2.0.docx` (NV-01, NV-03..05), `Dac_ta_ky_thuat_v3.0.docx` (§1, §3, §4, §12, §13, §14, §15)

---

## 0. Tóm tắt quyết định

| # | Chủ đề | Quyết định |
|---|---|---|
| D1 | Xác thực (authentication) | **Gateway** validate JWT + kiểm tra phiên còn sống trên mọi request. **BE validate lại** JWT (chữ ký/exp/iss/aud, không tra Redis/DB) làm lớp phòng thủ |
| D2 | Phân quyền (authorization) | **BE**, policy ở endpoint `[HasPermission(...)]`; quyền theo tài nguyên qua `IAuthorizationService` (module sau) |
| D3 | Phát token | **BE** (module IdentityAccess). Gateway không phát token |
| D4 | Thuật toán ký | **HS256**, 1 secret chung Gateway + BE, lấy từ secret store |
| D5 | Access token | 15 phút, **chỉ giữ trong RAM** trình duyệt |
| D6 | Refresh token | Random 256-bit, cookie `__Host-rt` HttpOnly/Secure/SameSite=Strict, DB chỉ lưu SHA-256; rotation mỗi lần refresh, **strict reuse** |
| D7 | Gia hạn | Access hết hạn → FE tự refresh (chủ động trước 60s + dự phòng khi gặp 401). Phiên có **hạn tuyệt đối 7 ngày**, không trượt, không idle timeout |
| D8 | Mô hình quyền | **Nhiều role/user** + **quyền lẻ chỉ cấp thêm** (không deny), không thời hạn, **toàn cục** (chưa theo cơ sở) |
| D9 | Permission trong Redis | `perm:{userId}`, **không TTL**, xoá chủ động qua bảng `CacheInvalidations` + worker |
| D10 | Thu hồi phiên | Gateway đọc `session:{familyId}` ở Redis; miss/lỗi → hỏi BE `/internal/sessions/validate` (DB) |
| D11 | Redis sập | **Rơi về DB**, hệ thống chậm nhưng vẫn chạy; health check `Degraded` |
| D12 | Quyền cho FE | JWT không chứa role/permission; FE lấy qua `GET /auth/me` |
| D13 | Rate limit | Theo IP ở **Gateway**. Bộ đếm theo email ở BE (`rl:email`, 5 lần sai/15 phút → 429) đã bị **gỡ bỏ** (quyết định 2026-09-30, spec V2 §2 mục 8) — login khoá hàng `User` (FOR UPDATE) và kiểm lại hash/IsActive sau khoá thay cho đếm số lần sai; BE không còn trả 429 |
| D14 | Audit | 2 bảng: `AuditLogs` (diff dữ liệu, giữ) + `AuditRecords` (truy cập/bảo mật, mới). **Bỏ** `UserLoginHistories` |
| D15 | Logging | Serilog: Console text + File CompactJson + **Seq** |
| D16 | Seed | 9 role theo đặc tả nghiệp vụ; spec này chỉ định nghĩa permission của IdentityAccess |
| D17 | Mật khẩu | 10–128 ký tự, khác mật khẩu hiện tại, không chứa email; không bắt đổi định kỳ |
| D18 | Tài khoản | Admin tạo tài khoản (mật khẩu tạm) · bắt đổi mật khẩu lần đầu · user tự đổi mật khẩu · Admin khoá/mở khoá |

### 0.1 Chỗ lệch so với Đặc tả kỹ thuật v3.0 — cần cập nhật docx

| Đặc tả v3.0 | Spec này | Lý do |
|---|---|---|
| §1.1 "Gateway định tuyến…; **API xác thực** và quyết định quyền" | Gateway xác thực, API validate lại + quyết định quyền | Chặn request không hợp lệ sớm; BE vẫn không tin header nào từ client (D1) |
| §3.2 "Không cache quyền theo TTL dài ở MVP" | Cache quyền trong Redis **không TTL** nhưng xoá chủ động, bảo đảm bằng bảng chờ + worker | Đúng tinh thần "thay đổi có hiệu lực với request mới"; độ trễ xấu nhất vài giây |
| §4.2 "validation kiểm tra family và SecurityVersion **từ DB**" | Kiểm tra từ Redis, miss/lỗi mới xuống DB | Giảm tải DB mỗi request; DB vẫn là nguồn sự thật |
| §13.4 "Redis chỉ bổ sung khi có nhu cầu thực tế" | Bổ sung Redis | Nhu cầu: cache phiên/quyền cho Gateway + rate limit dùng chung |
| §1.2 SQL Server | PostgreSQL | Đã chốt trước đó trong `00-quyet-dinh-va-quy-uoc.md` |

---

## 1. Kiến trúc

```
React ──► Gateway (YARP) ─────────────► API (BE) ──► PostgreSQL
 RAM:        │ 1. Rate limit IP (Redis)   │ 4. Validate lại JWT (chữ ký/exp)
 accessToken │ 2. Validate JWT HS256      │ 5. [HasPermission] → perm:{userId} (Redis → DB)
 Cookie:     │ 3. Check session:{fid}     │ 6. Quyền tài nguyên (module sau)
 __Host-rt   │    (Redis → miss → hỏi BE) │ 7. Phát token, rotation, audit
 __Host-csrf └──────────── Redis ─────────┘
```

FE và Gateway **cùng origin** (§4.1 MVP). BE không được truy cập trực tiếp từ Internet (§1.1) — cấu hình triển khai phải bảo đảm.

### 1.1 Trách nhiệm

**Gateway** — chỉ xác thực + định tuyến, không tham chiếu project nào khác, không biết nghiệp vụ.
- Route public (không cần access token): `POST /api/v1/auth/login`, `POST /api/v1/auth/refresh`, `POST /api/v1/auth/logout`.
- Mọi route còn lại: JWT hợp lệ + phiên còn sống, nếu không → 401, request **không** tới BE.
- Xoá mọi header `X-Internal-*` từ client. Forward nguyên `Authorization`, `X-Correlation-Id`.
- Không có route nào ra ngoài cho `/internal/*`.

**BE (API)** — phát token, quản lý phiên, phân quyền, audit.
- Validate lại JWT (chữ ký, alg, iss, aud, exp, nbf) — **không** tra phiên.
- Là nơi **duy nhất** ghi/xoá `session:*` và `perm:*` trong Redis. Gateway chỉ đọc.

**Redis** — cache, không phải nguồn sự thật.

| Key | Giá trị | TTL | Ai ghi |
|---|---|---|---|
| `session:{familyId}` | `{ userId, sv, absExp }` | = `AbsoluteExpiresAtUtc` | BE |
| `perm:{userId}` | `{ permissions: string[], mustChangePassword: bool }` | không | BE (khi cache miss) |
| `rl:ip:{route}:{ip}` | bộ đếm fixed window | = cửa sổ | Gateway |
| ~~`rl:email:{sha256(emailChuẩnHoá)}`~~ | đã gỡ bỏ (quyết định 2026-09-30, spec V2 §2 mục 8) — login khoá hàng `User` thay cho đếm số lần sai | — | — |

### 1.2 JWT

Header `alg=HS256`. Claims: `sub` (userId), `fid` (familyId), `sv` (SecurityVersion), `jti`, `iat`, `nbf`, `exp` (+15 phút), `iss`, `aud`. **Không** chứa role, permission, email, họ tên (§3: JWT chỉ giữ claim ổn định). `ClockSkew` = 30 giây ở cả Gateway và BE.

### 1.3 Hạ tầng dev

`docker-compose.yml` ở gốc `benh_vien_be/`: `postgres`, `redis`, `seq`. Secret (JWT key, CSRF key, Internal key) qua User Secrets ở dev, biến môi trường/secret store ở production — không commit.

---

## 2. Mô hình dữ liệu (PostgreSQL)

### 2.1 Quyền

| Bảng | Cột | Ràng buộc / ghi chú |
|---|---|---|
| `Users` | Id, Email, PasswordHash, FullName, AvatarUrl, IsActive, **MustChangePassword**, **SecurityVersion** (int, mặc định 1), LastLoginAt, CreatedAt, UpdatedAt | Email unique (đã chuẩn hoá). **Bỏ cột `Role`** |
| `Roles` | Id, Code, Name, IsSystem | Code unique, `lower-kebab` |
| `Permissions` | **Code** (PK), Group, Description | Danh mục định nghĩa trong code, đồng bộ vào DB lúc khởi động (thêm mã mới, cập nhật mô tả; **không** tự xoá mã cũ — log warning) |
| `RolePermissions` | RoleId, PermissionCode | PK (RoleId, PermissionCode) |
| `UserRoles` | UserId, RoleId, FacilityId (nullable, chưa dùng), AssignedAtUtc, AssignedBy | Unique (UserId, RoleId) |
| `UserPermissions` | UserId, PermissionCode, GrantedAtUtc, GrantedBy, Reason | PK (UserId, PermissionCode). Chỉ cấp thêm |

**Quyền hiệu lực** = (∪ permission của mọi role của user) ∪ (UserPermissions); rỗng nếu `IsActive = false`.

### 2.2 Phiên (§4.2)

| Bảng | Cột | Ràng buộc |
|---|---|---|
| `SessionFamilies` | Id, UserId, Status (`Active`/`Revoked`), CreatedAtUtc, AbsoluteExpiresAtUtc, RevokedAtUtc, RevokeReason (`Logout`/`LogoutAll`/`Reuse`/`UserRevoked`/`PasswordChanged`/`AccountDeactivated`), IpAddress, UserAgent, LastRefreshedAtUtc | Index (UserId, Status) |
| `RefreshTokens` | Id, FamilyId (FK), TokenHash, CreatedAtUtc, ExpiresAtUtc, ConsumedAtUtc, RevokedAtUtc, ReplacedById | TokenHash unique; `ExpiresAtUtc` = `AbsoluteExpiresAtUtc` của family |

Khoá hàng: `SELECT … FOR UPDATE` theo thứ tự **family → token**, mọi thao tác (refresh, logout, logout-all, revoke, khoá tài khoản, đổi mật khẩu) đều theo cùng thứ tự.

### 2.3 Audit & hạ tầng

| Bảng | Cột |
|---|---|
| `AuditLogs` (giữ) | + **CorrelationId**. Áp `IAuditable` thêm cho `Role`, `UserRole`, `UserPermission`, `RolePermission`. **Không** áp cho `SessionFamily`/`RefreshToken` (mỗi lần refresh sẽ sinh diff rác — sự kiện phiên đã có AuditRecord) |
| `AuditRecords` (mới) | Id, ActorId?, Action, ResourceType?, ResourceId?, Result (`Succeeded`/`Failed`/`Denied`), Reason?, CorrelationId, IpAddress?, UserAgent?, TimestampUtc, Metadata (jsonb, ≤ 4 KB, không PHI/token/mật khẩu) — index (ActorId, TimestampUtc), (Action, TimestampUtc) |
| `CacheInvalidations` (mới) | Id, Key, CreatedAtUtc, Attempts, LastError? |
| `UserLoginHistories` | **Xoá** (entity, repository, configuration, test) |

### 2.4 Domain

- **`User`** (aggregate): sở hữu `UserRoles`, `UserPermissions`.
  - `Create(email, fullName, passwordHash)` → `MustChangePassword = true`.
  - `SetRoles(roleIds, by)`, `GrantPermission(code, by, reason)`, `RevokePermission(code)`.
  - `ChangePassword(newHash)` → `SecurityVersion++`, `MustChangePassword = false`.
  - `Deactivate()` → `IsActive = false`, `SecurityVersion++`. `Activate()`.
  - `RecordLogin()` (giữ).
  - Raise domain event tương ứng (dùng các event sẵn có + `UserRolesChanged`, `UserPermissionsChanged`).
- **`Role`** (aggregate): sở hữu `RolePermissions`. `Create(code, name)`, `Rename(name)`, `SetPermissions(codes)`. Role `IsSystem` không đổi `Code`.
- **`SessionFamily`** (aggregate): sở hữu `RefreshTokens`.
  - `Start(userId, tokenHash, now, ip, ua)` → hạn tuyệt đối = now + 7 ngày.
  - `Rotate(presentedHash, newHash, now)` → trả kết quả `Rotated` / `ReuseDetected` / `Expired` / `NotActive`.
  - `Revoke(reason, now)` → revoke family + mọi token chưa consumed.
- Hằng số: `Domain/Identity/Permissions.cs` (danh mục mã), `Domain/Identity/SystemRoles.cs` (9 mã role). **Xoá** `Domain/Constants/Roles.cs`.

### 2.5 Seed

**9 role** (`IsSystem = true`):

| Code | Tên |
|---|---|
| `receptionist` | Lễ tân |
| `outpatient-nurse` | Điều dưỡng ngoại trú |
| `doctor` | Bác sĩ |
| `inpatient-nurse` | Điều dưỡng nội trú |
| `lab-technician` | KTV CLS |
| `pharmacist` | Dược sĩ |
| `cashier` | Thu ngân |
| `clinical-manager` | Quản lý chuyên môn |
| `admin` | Admin |

**Permission IdentityAccess** (gán hết cho `admin`): `users.read`, `users.create`, `users.activate`, `users.roles.manage`, `users.permissions.manage`, `roles.read`, `roles.manage`, `permissions.read`.

**Tài khoản Admin đầu tiên**: email + mật khẩu tạm từ cấu hình (`Seed:AdminEmail`, `Seed:AdminPassword`), `MustChangePassword = true`. Chỉ tạo nếu chưa có user nào mang role `admin`.

Module nghiệp vụ sau tự thêm permission của mình vào `Permissions.cs` + mapping role trong seed của module đó.

---

## 3. Luồng xác thực & phiên

Tiền tố `/api/v1`. Mọi response lỗi là Problem Details (§6).

### 3.1 Cookie & CSRF

| Cookie | Thuộc tính | Giá trị |
|---|---|---|
| `__Host-rt` | HttpOnly, Secure, SameSite=Strict, Path=/, không Domain, Max-Age = thời gian còn lại của family | refresh token (base64url, 32 byte random) |
| `__Host-csrf` | **không** HttpOnly, Secure, SameSite=Strict, Path=/, không Domain, cùng Max-Age | `base64url(HMAC-SHA256(familyId, CsrfKey))` |

Endpoint dùng cookie (`/refresh`, `/logout`) và các endpoint thay đổi phiên (`/logout-all`, `/sessions/{id}/revoke`, `/change-password`) bắt buộc:
1. Header `Origin` thuộc allowlist cấu hình (`Auth:AllowedOrigins`).
2. Header `X-CSRF-Token` = HMAC của family (với `/refresh`, `/logout`: family của refresh token trong cookie; với endpoint cần access token: family trong claim `fid`).
3. Sai bất kỳ → **403** `code: csrf_failed`.

### 3.2 Login — `POST /auth/login` `{ email, password }`

1. Gateway: rate limit IP (§4.4).
2. BE: validator (email đúng định dạng, password không rỗng — **không** kiểm độ mạnh).
3. Định vị user theo email (không khoá); trong **1 transaction**: khoá hàng `User` (FOR UPDATE) rồi kiểm lại hash/`IsActive` trên dữ liệu mới nhất đã commit (không còn bộ đếm `rl:email`/429 — quyết định 2026-09-30, spec V2 §2 mục 8).
4. Không có user / `IsActive = false` / sai mật khẩu → AuditRecord `auth.login` `Failed` (Reason `EmailNotFound`/`AccountInactive`/`InvalidPassword`, ActorId = userId nếu có), **commit**, trả **401** `"Email hoặc mật khẩu không đúng."` — message giống hệt cho cả 3 nhánh.
5. Đúng: cùng transaction ở bước 3: `user.RecordLogin()`, `SessionFamily.Start(...)`, AuditRecord `auth.login` `Succeeded`; commit **một lần**.
6. Sau commit: ghi `session:{fid}` vào Redis (lỗi → log Warning, không fail request).
7. `200 { accessToken, expiresAtUtc, mustChangePassword }` + `Set-Cookie` `__Host-rt`, `__Host-csrf`.

### 3.3 Refresh — `POST /auth/refresh` (cookie + CSRF, không cần access token)

Trong transaction:
1. Hash token từ cookie → tìm `RefreshTokens` → khoá family → khoá token.
2. Không tìm thấy → **401** + xoá cookie.
3. Token đã `Consumed`/`Revoked` → **reuse**: `family.Revoke(Reuse)`, AuditRecord `auth.refresh.reuse` `Denied`, chèn `CacheInvalidations(session:{fid})`, **commit**, xoá Redis, rồi **401** + xoá cookie. Không throw trước commit (§4.2).
4. Family `Revoked` hoặc quá `AbsoluteExpiresAtUtc`, hoặc user `IsActive = false` → **401** + xoá cookie.
5. Hợp lệ: token cũ `ConsumedAtUtc = now`, chèn token mới, `ReplacedById`, `LastRefreshedAtUtc = now`; commit.
6. `200 { accessToken, expiresAtUtc, mustChangePassword }` + cookie mới (`__Host-csrf` giữ nguyên giá trị vì cùng family).

Refresh thành công **không** ghi AuditRecord (chỉ log Debug).

### 3.4 Logout — `POST /auth/logout` (cookie + CSRF)

Revoke family của cookie (`Logout`), AuditRecord `auth.logout`, `CacheInvalidations(session:{fid})`, commit, xoá Redis, xoá 2 cookie → **204**. Cookie thiếu/không hợp lệ → vẫn xoá cookie, **204** (idempotent).

### 3.5 Logout-all — `POST /auth/logout-all` (access token + CSRF)

Revoke **mọi** family Active của user (`LogoutAll`), AuditRecord, invalidation cho từng `session:{fid}`, commit, xoá Redis, xoá cookie → **204**.

### 3.6 Me — `GET /auth/me` (access token)

`200 { id, email, fullName, avatarUrl, roles: [{code, name}], permissions: string[], mustChangePassword }`. **Không bao giờ** có `passwordHash`. Map thủ công.

### 3.7 Sessions

- `GET /auth/sessions` → `[{ id, createdAtUtc, lastRefreshedAtUtc, absoluteExpiresAtUtc, ipAddress, userAgent, isCurrent }]` — chỉ family `Active` chưa hết hạn của user hiện tại; `isCurrent` = (id == claim `fid`).
- `POST /auth/sessions/{id}/revoke` (CSRF) → revoke (`UserRevoked`), AuditRecord, invalidation → **204**. Family không thuộc user → **404** (không phải 403).

### 3.8 Đổi mật khẩu — `POST /auth/change-password` `{ currentPassword, newPassword }` (access token + CSRF)

1. Sai `currentPassword` → **400** validation (`errors.currentPassword`), không 401.
2. `newPassword`: 10–128 ký tự, ≠ mật khẩu hiện tại, không chứa phần local của email (không phân biệt hoa thường).
3. Transaction: `user.ChangePassword(hash)` (`SecurityVersion++`, `MustChangePassword = false`), revoke **mọi family khác** (`PasswordChanged`), invalidation `session:*` của **mọi** family (kể cả phiên hiện tại — key cũ mang `sv` cũ) + `perm:{uid}`, AuditRecord `auth.password.change`; commit; xoá Redis.
4. Không ghi lại `session:{currentFid}` ở đây: request kế tiếp Gateway gặp miss, hỏi BE (§3.10) và nạp lại với `sv` mới qua đường ghi có điều kiện (§4.3) — tránh race với thao tác thu hồi đồng thời.
5. `200 { accessToken, expiresAtUtc, mustChangePassword: false }` — access token mới mang `sv` mới (token cũ của phiên hiện tại bị Gateway chặn vì lệch `sv`).

### 3.9 Bắt đổi mật khẩu lần đầu

Policy toàn cục ở BE (chạy sau authentication): nếu `perm:{uid}.mustChangePassword = true` → **403** `code: password_change_required` cho mọi endpoint **trừ** `GET /auth/me`, `POST /auth/change-password`, `POST /auth/logout`, `POST /auth/logout-all`.

### 3.10 Internal — `POST /internal/sessions/validate` `{ familyId, sv }`

Chỉ BE, yêu cầu header `X-Internal-Key` khớp cấu hình (so sánh constant-time; sai/thiếu → 401). Đọc DB: family `Active`, chưa hết hạn, user `IsActive`, `Users.SecurityVersion == sv` → `200 { valid: true, userId, absExp }` và BE ghi lại `session:{fid}` vào Redis bằng đường ghi có điều kiện theo thế hệ (§4.3 bước 4; nếu Redis sống); ngược lại `200 { valid: false }` và không ghi cache.

---

## 4. Phân quyền (BE) & Gateway

### 4.1 Authorize ở BE

- `[HasPermission(Permissions.Users.Create)]` → `PermissionPolicyProvider` tạo policy `perm:<code>` → `PermissionAuthorizationHandler` gọi `IPermissionService.GetAsync(userId, ct)`:
  - đọc `perm:{userId}` → có thì dùng;
  - miss → tính quyền hiệu lực từ DB → ghi Redis;
  - Redis lỗi → đọc DB, log Warning.
- `FallbackPolicy` = yêu cầu đăng nhập. Ẩn danh phải ghi rõ `[AllowAnonymous]` (chỉ login/refresh/logout; internal dùng filter riêng cho `X-Internal-Key`).
- Từ chối → **403** `forbidden` + AuditRecord `authz.denied` `Denied` (Metadata: permission, path), ghi tại `IAuthorizationMiddlewareResultHandler` trong scope request (chưa có transaction nghiệp vụ nào ở bước này).
- Quyền theo tài nguyên: **ngoài phạm vi**; chỉ bảo đảm `IAuthorizationService` đã đăng ký để module sau thêm resource handler.

### 4.2 API quản trị

| Endpoint | Permission | Ghi chú |
|---|---|---|
| `GET /users?pageNumber&pageSize&searchTerm` | `users.read` | `PagedResult<UserSummaryDto>` |
| `GET /users/{id}` | `users.read` | kèm roles, permissions cấp thêm |
| `POST /users` `{ email, fullName, temporaryPassword, roleIds[] }` | `users.create` | `MustChangePassword = true`; email trùng → 409 `email_taken` |
| `POST /users/{id}/deactivate` | `users.activate` | `SecurityVersion++`, revoke mọi family (`AccountDeactivated`), invalidation `perm` + `session:*` |
| `POST /users/{id}/activate` | `users.activate` | |
| `PUT /users/{id}/roles` `{ roleIds[] }` | `users.roles.manage` | thay nguyên tập; invalidation `perm:{uid}` |
| `POST /users/{id}/permissions/grant` `{ permissionCode, reason }` | `users.permissions.manage` | mã không có trong danh mục → 400 |
| `POST /users/{id}/permissions/revoke` `{ permissionCode, reason }` | `users.permissions.manage` | |
| `GET /roles` | `roles.read` | kèm permissions |
| `POST /roles` `{ code, name, permissionCodes[] }` | `roles.manage` | |
| `PUT /roles/{id}` `{ name }` | `roles.manage` | |
| `PUT /roles/{id}/permissions` `{ permissionCodes[] }` | `roles.manage` | invalidation `perm:{uid}` cho **mọi user thuộc role** |
| `GET /permissions` | `permissions.read` | danh mục, nhóm theo `Group` |

Mọi command ở đây ghi AuditRecord (`users.create`, `users.deactivate`, `users.activate`, `users.roles.set`, `users.permissions.grant`, `users.permissions.revoke`, `roles.create`, `roles.update`, `roles.permissions.set`) và AuditLogs tự sinh diff qua interceptor.

**Quy tắc bảo vệ** (vi phạm → 409):
- `self_action_forbidden`: không tự khoá chính mình, không tự gỡ role `admin` của mình.
- `last_admin`: sau thao tác phải còn ≥ 1 user `IsActive` có role `admin`.
- Role `IsSystem`: không đổi `Code`, không xoá (xoá role nằm ngoài phạm vi).

### 4.3 Xoá cache tin cậy

1. Handler nghiệp vụ gọi `ICacheInvalidator.Enqueue(key)` → chèn dòng `CacheInvalidations` vào **cùng DbContext/transaction**.
2. Sau `SaveChangesAsync` thành công: `ICacheInvalidator.FlushAsync()` xoá key trong Redis và xoá dòng tương ứng.
3. `CacheInvalidationWorker` (`BackgroundService` trong API): khi khởi động và mỗi 5 giây, lấy tối đa 100 dòng cũ nhất, xoá key, xoá dòng; lỗi → `Attempts++`, `LastError`, log Warning (Error khi `Attempts ≥ 10`, không bỏ dòng).
4. **Chặn race "ghi lại dữ liệu cũ"** (cache không TTL): mỗi lần xoá key `K` đồng thời `INCR K:gen` (TTL 1 ngày). Mọi lần ghi cache từ DB (PermissionService, endpoint validate nội bộ) đọc `K:gen` **trước** khi truy vấn DB và chỉ ghi nếu `K:gen` chưa đổi (Redis transaction có điều kiện). Nếu không, request đọc DB cũ có thể ghi đè sau khi key vừa bị xoá và dữ liệu sai tồn tại mãi. Login ghi `session:{fid}` của family mới tạo **cũng có race**: sau commit, thao tác thu hồi chạy xen (khoá tài khoản → revoke family, xoá key, `INCR :gen`) rồi login mới ghi ⇒ `sv` cũ sống tới hết hạn tuyệt đối. Vì vậy login cũng ghi có điều kiện: chỉ ghi khi `session:{fid}:gen` **chưa tồn tại** (cập nhật 2026-09-28, plan `2026-09-28-login-khung-moi` Q3).

| Sự kiện | Key |
|---|---|
| Đổi role / quyền lẻ của user | `perm:{uid}` |
| Đổi permission của role | `perm:{uid}` × mọi user thuộc role |
| Khoá / mở khoá tài khoản | `perm:{uid}` (+ `session:{fid}` × mọi family khi khoá) |
| Đổi mật khẩu | `perm:{uid}` + `session:{fid}` × mọi family, kể cả family hiện tại |
| Logout / logout-all / revoke / reuse | `session:{fid}` |

> Cập nhật 2026-09-30 (plan `plan-01-doi-mat-khau`): đổi mật khẩu tăng `sv` nên key `session:{fid}` của **cả family hiện tại** cũng phải xoá, nếu không Gateway còn nhận token cũ (sv cũ) và từ chối token mới từ cache. Handler không ghi lại `session:{currentFid}`; request kế tiếp với token mới để Gateway hỏi API rồi nạp lại. Nếu flush sau commit thất bại, cache còn sv cũ cho tới khi `CacheInvalidationWorker` xử lý (chu kỳ 5 giây, có retry) — trong khoảng đó token cũ vẫn qua Gateway và token mới bị 401; không phải thu hồi "ngay lập tức".

### 4.4 Gateway

- `AddAuthentication().AddJwtBearer(...)`: HS256, validate alg/iss/aud/lifetime/signature, `ClockSkew = 30s`, `MapInboundClaims = false`.
- `OnTokenValidated` → `ISessionValidator.ValidateAsync(fid, sv)`:
  - Redis `session:{fid}` có → hợp lệ nếu `sv` khớp và chưa quá `absExp`;
  - miss hoặc Redis lỗi → `POST {BE}/internal/sessions/validate` (HttpClient, timeout 2s, header `X-Internal-Key`);
  - BE không phản hồi → **503** `dependency_unavailable`;
  - không hợp lệ → **401**.
- YARP: route `auth-public` (login/refresh/logout) `AuthorizationPolicy = "anonymous"`; mọi route khác `AuthorizationPolicy = "default"` (authenticated). Transform xoá `X-Internal-*` trên mọi route.
- Rate limit IP (fixed window, bộ đếm Redis): `/auth/login` 10/phút/IP, `/auth/refresh` 30/phút/IP → **429** + `Retry-After`. Redis lỗi → cho qua + log Warning.
- IP client: chỉ đọc `X-Forwarded-For` từ proxy tin cậy cấu hình (`ForwardedHeaders:KnownProxies`).

---

## 5. Frontend (`benh_vien_fe`)

Module `src/features/auth/`:
- **`tokenStore`**: access token + `expiresAtUtc` trong biến module (closure). Không Redux-persist, không localStorage/sessionStorage.
- **Redux slice `auth`**: `status` (`booting`/`authenticated`/`anonymous`), `user`, `permissions`, `mustChangePassword`.
- **Boot / F5**: `POST /auth/refresh` → thành công → `GET /auth/me` → `authenticated`; 401 → `anonymous` → `/login`.
- **Refresh chủ động**: hẹn giờ tại `exp − 60s`.
- **Interceptor axios**: gắn `Authorization`; gặp 401 → refresh **một lần** → gửi lại request; vẫn 401 → logout cục bộ + thông báo "Phiên đăng nhập đã hết hiệu lực". Không lặp vô hạn.
- **Single-flight**: trong tab, mọi lời gọi refresh dùng chung 1 promise. Đa tab: `navigator.locks.request('auth-refresh', …)`; tab refresh xong phát `{type:'token', accessToken, expiresAtUtc}` qua `BroadcastChannel('auth')`; tab khác nhận thì cập nhật `tokenStore`, không tự refresh. Không hỗ trợ Web Locks → chấp nhận rủi ro reuse (§4.3).
- **CSRF**: đọc cookie `__Host-csrf`, gửi `X-CSRF-Token` cho các endpoint ở §3.1.
- **Logout**: gọi `/auth/logout`, broadcast `{type:'logout'}`, mọi tab xoá `tokenStore`, Redux, query cache → `/login`.
- **403 `password_change_required`** → chuyển `/change-password`.
- `hasPermission(code)` / `<Can permission=…>` chỉ để ẩn UI.
- Login: form email + mật khẩu; 401/429 hiển thị message server trả về, không phân biệt nguyên nhân.

---

## 6. Audit, Logging, Lỗi

### 6.1 AuditRecord

- `IAuditRecorder.Record(action, result, reason?, resourceType?, resourceId?, metadata?)` ở Application; Infrastructure điền `ActorId` (ICurrentUser), `CorrelationId`, `IpAddress`, `UserAgent`, `TimestampUtc` và thêm vào DbContext → ghi **cùng transaction** với nghiệp vụ.
- Nhánh thất bại cần lưu (401 login, 429, reuse) → handler tự `SaveChangesAsync` **trước** khi trả lỗi.
- `authz.denied` ghi trong `ProblemAuthorizationResultHandler` (scope request — chưa có transaction nghiệp vụ ở bước authorize).
- Hạ tầng cho module sau: interface `IAuditedRequest` + `AuditBehavior` (MediatR) ghi `Authorized` trước khi handler trả dữ liệu; ghi audit lỗi → throw, không trả dữ liệu (§3.3). Spec này chưa có request nào implement.

### 6.2 AuditLogs

Interceptor giữ nguyên, thêm `CorrelationId`; bỏ qua property `PasswordHash`, `TokenHash`.

### 6.3 Logging

- Serilog ở Gateway và API: Console (text, template có `CorrelationId`), File (`CompactJsonFormatter`, rolling theo ngày, giữ 14 file), **Seq** (`Seq:ServerUrl`).
- Enricher: `CorrelationId`, `UserId`, `SessionFamilyId`, `Service` (`gateway`/`api`), `RequestPath`.
- Không ghi: body request; header `Authorization`, `Cookie`, `Set-Cookie`, `X-CSRF-Token`, `X-Internal-Key`; mọi property tên `password`, `currentPassword`, `newPassword`, `temporaryPassword`, `accessToken`, `refreshToken` (destructuring policy thay bằng `***`).
- `LoggingBehavior`: chỉ tên request + thời gian (giữ như hiện tại).

### 6.4 Lỗi — Problem Details (RFC 9457)

`GlobalExceptionHandler` (API) trả `{ type, title, status, code, traceId, errors? }`, `traceId` = CorrelationId. Không stack trace, không SQL. Gateway trả cùng format cho 401/429/503 của nó.

| Status | `code` |
|---|---|
| 400 | `validation_failed` (kèm `errors`) |
| 401 | `unauthenticated` |
| 403 | `forbidden`, `password_change_required`, `csrf_failed` |
| 404 | `not_found` |
| 409 | `email_taken`, `last_admin`, `self_action_forbidden`, `conflict` |
| 429 | `rate_limited` (+ `Retry-After`) |
| 503 | `dependency_unavailable` |

### 6.5 Health check

`/health` (API, Gateway): PostgreSQL (API), Redis → `Degraded` khi Redis lỗi (không `Unhealthy`).

---

## 7. Kiểm thử

**Unit** (`CleanArchCqrs.UnitTests`)
- `SessionFamily`: `Start`, `Rotate` (Rotated / ReuseDetected / Expired / NotActive), `Revoke`, không vượt hạn tuyệt đối.
- `User`: `SetRoles`, `GrantPermission`/`RevokePermission`, `ChangePassword`/`Deactivate` tăng `SecurityVersion`.
- Tính quyền hiệu lực; validator mật khẩu; CSRF HMAC; quy tắc last-admin.

**Integration** (project mới `CleanArchCqrs.IntegrationTests`, **Testcontainers PostgreSQL + Redis**, `WebApplicationFactory`; không dùng EF InMemory làm bằng chứng — §14.1)
- Login đúng/sai/không tồn tại/khoá → message 401 giống hệt; AuditRecord đúng Reason.
- Bộ đếm khoá tạm theo email đã bị gỡ bỏ (quyết định 2026-09-30) — không còn ca "5 lần sai → lần 6 trả 429" ở BE;
  chỉ còn rate limit IP ở Gateway.
- Refresh rotation; **2 refresh đồng thời cùng token → đúng 1 thành công, family bị revoke** (strict reuse).
- Token cũ trình lại → family bị revoke, AuditRecord `auth.refresh.reuse` còn sau khi trả 401.
- Refresh không vượt hạn family.
- Đổi quyền role → request kế tiếp của user nhận quyền mới.
- Khoá tài khoản → access token cũ bị từ chối ngay qua Gateway.
- Đổi mật khẩu → phiên khác chết, phiên hiện tại sống với token mới.
- Dừng container Redis → login, refresh, request có quyền vẫn chạy.
- `CacheInvalidations` tồn đọng được worker xử lý.
- CSRF sai / Origin sai → 403.
- `MustChangePassword` chặn endpoint khác.
- Last-admin / self-action → 409.

**Gateway** (`WebApplicationFactory<Gateway.Program>` + backend giả)
- Không token / token sai chữ ký / hết hạn / `sv` lệch → 401, backend giả không nhận request.
- Route public đi qua không cần token.
- Header `X-Internal-*` của client bị xoá.
- Redis miss → gọi internal validate.

**FE**: jest cho `tokenStore`, single-flight refresh, interceptor 401 → refresh 1 lần.

---

## 8. Ngoài phạm vi (backlog)

Admin đặt lại mật khẩu · quên mật khẩu qua email · MFA · quyền theo cơ sở/khoa · AccessGrant & CareTeamAssignment · API xem audit · xoá role · Admin thu hồi phiên của người khác · cảnh báo đăng nhập bất thường.

---

## 9. Tiêu chí nghiệm thu

- [ ] `dotnet build` 0 error, 0 warning; toàn bộ test pass.
- [ ] Login qua Gateway → 200, response có access token, trình duyệt nhận `__Host-rt` (HttpOnly) + `__Host-csrf`.
- [ ] JWT chỉ có `sub, fid, sv, jti, iat, nbf, exp, iss, aud`.
- [ ] F5 trang → vẫn đăng nhập (qua refresh), localStorage/sessionStorage không có token.
- [ ] Để access hết hạn khi đang thao tác → không bị đá ra, request không lỗi.
- [ ] Sau 7 ngày kể từ login → bắt đăng nhập lại dù đang dùng.
- [ ] Logout → access token cũ bị Gateway trả 401 ngay.
- [ ] Gỡ permission khỏi role → request kế tiếp của user bị 403.
- [ ] Gọi thẳng BE bằng token giả chữ ký → 401.
- [ ] Tắt Redis → hệ thống vẫn dùng được, `/health` báo Degraded.
- [ ] Seq tìm được 1 request theo CorrelationId ở cả log Gateway và API; không dòng log nào chứa mật khẩu/token.
- [ ] Admin mới seed đăng nhập lần đầu bị bắt đổi mật khẩu.

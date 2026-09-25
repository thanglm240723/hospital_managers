# Kiến trúc — Hệ thống quản lý khám chữa bệnh (HMS)

Tài liệu mô tả kiến trúc **đang chạy** trong repo. Quyết định gốc nằm trong `Dac_ta_ky_thuat_v3.1.docx`;
chỗ lệch có chủ đích được ghi trong spec theo chủ đề (`docs/superpowers/plans/<chủ-đề>/spec.md`).

## 1. Thành phần triển khai

```
Trình duyệt ── React (:9000) ──► Gateway YARP (:5100) ──► CleanArchCqrs.API (:5289) ──► PostgreSQL 17 (:5433)
                 access token      │ rate limit theo IP        │ validate lại JWT               ▲
                 trong RAM         │ validate JWT HS256        │ [HasPermission] (Redis → DB)   │
                 cookie __Host-rt  │ phiên session:{fid}       │ phát token, rotation, audit    │
                 cookie __Host-csrf│   Redis → miss → hỏi API  │ CacheInvalidationWorker ───────┘
                                   └──────── Redis 7.4 (:6379) ┘
                                   Serilog → Console · File (CompactJson) · Seq (:5341)
```

| Thành phần | Công nghệ | Cổng dev | Ghi chú |
|---|---|---|---|
| Frontend | React 16, Redux, redux-observable, axios, Jest | 9000 | Node 24; cùng origin với Gateway qua proxy dev |
| Gateway | ASP.NET Core 10 + YARP 2.3 | 5100 / 7100 | Điểm vào công khai duy nhất; không tham chiếu project nào |
| API | ASP.NET Core 10, MediatR 12.5, FluentValidation, EF Core 10 | 5289 / 7163 | Monolith chia module; Swagger ở `/` khi Development |
| PostgreSQL | 17-alpine (Docker) | 5433 → 5432 | Nguồn sự thật nghiệp vụ. Cổng host 5433 vì máy dev có PostgreSQL cài sẵn ở 5432 |
| Redis | 7.4-alpine | 6379 | Cache phiên/quyền, rate limit. Không phải nguồn sự thật |
| Seq | 2024.3 | 5341 | Xem log có cấu trúc khi dev |

`run.ps1` ở thư mục gốc khởi động tất cả. Docker Compose nằm ở `dotnet-clean-architecture-cqrs-starter/docker-compose.yml`
(project name `hospital-management`, container `hospital-*`).

## 2. Layer backend

```
CleanArchCqrs.Domain          Entity<TId>, AggregateRoot<TId>, DomainEvent, IUnitOfWork, exception miền,
                              Identity/ (User, Role, Permission, SessionFamily, RefreshToken, Permissions catalog)
        ▲
CleanArchCqrs.Application     <Feature>/{Commands,Queries,Validators,Models}/ · Common/{Behaviors,Exceptions,
                              Interfaces,Models,Security,Auditing}
        ▲
CleanArchCqrs.Infrastructure  Persistence/{AppDbContext, Configurations, Migrations, Interceptors, Seed},
                              Repositories/, Caching/ (Redis), Identity/, Security/, Auditing/, HealthChecks/
        ▲
CleanArchCqrs.API             Controllers/, Contracts/, Auth/ (cookie, CSRF, internal key), Authorization/
                              (HasPermission, policy provider), Errors/, Middleware/, Services/ (CurrentUser)

CleanArchCqrs.Gateway         độc lập — Auth/, Middleware/ (rate limit, correlation, strip headers), HealthChecks/
```

Pipeline MediatR: `LoggingBehavior` → `ValidationBehavior` → `AuditBehavior` (chỉ với `IAuditedRequest`).
Không có behavior transaction: handler tự quản transaction qua `IUnitOfWork` (xem §5).

## 3. Module

| Module (đặc tả §1.3) | Trạng thái | Mã nghiệp vụ |
|---|---|---|
| IdentityAccess — đăng nhập, phiên, người dùng, vai trò, quyền | **Đã có** (BE + FE login/đổi mật khẩu) | NV-03..05 |
| PatientRegistry — hồ sơ người bệnh, định danh | Chưa làm | NV-06..09 |
| ReceptionQueue — buổi khám, cấp số, gọi lượt | Chưa làm | NV-10..12 |
| Clinical — khám, chỉ định, kết quả, bệnh án, đơn thuốc | Chưa làm | NV-13..25 |
| Inpatient — nhập viện, giường, y lệnh, xuất viện | Chưa làm | NV-26..30 |
| Billing — hóa đơn, tạm ứng, thu tiền, quyết toán | Chưa làm | NV-31..36 |
| Documents — tệp bệnh án/CLS trên object storage | Chưa làm | NV-37..41 |
| Catalog, AuditNotifications, báo cáo | Chưa làm | NV-42..43 |

**Không có module đặt lịch** (đặc tả v3.x). Route Gateway `/api/appointments/**` và các route nghiệp vụ không có
`/v1` (`/api/patients/**`, `/api/doctors/**`, `/api/medical-records/**`, `/api/billing/**`) là cấu hình cũ — khi làm
module nào thì đổi route của module đó sang `/api/v1/<tài-nguyên>` theo §12.1.

## 4. Luồng xác thực và phân quyền

Chi tiết: `docs/superpowers/plans/2026-09-23-auth-permission-redesign/spec.md`.

1. `POST /api/v1/auth/login` → API kiểm tra email/mật khẩu (rate limit 5 lần sai/15 phút theo email), tạo
   `SessionFamily` + `RefreshToken` (DB chỉ lưu SHA-256), trả access token JWT HS256 15 phút + cookie `__Host-rt`
   (HttpOnly, Secure, SameSite=Strict) + cookie CSRF.
2. Mỗi request: Gateway validate JWT, đọc `session:{familyId}` trong Redis; miss/lỗi → `POST /internal/sessions/validate`
   (bảo vệ bằng header `X-Internal-Key`). API validate lại chữ ký JWT.
3. `[HasPermission]` đọc `perm:{userId}` trong Redis (không TTL); miss → tính từ DB (role + quyền lẻ).
4. `POST /api/v1/auth/refresh`: rotation nguyên tử, **strict reuse** — trình lại token đã dùng ⇒ thu hồi cả family.
   Phiên tuyệt đối 7 ngày.
5. Đổi mật khẩu/khóa tài khoản/đổi quyền: tăng `SecurityVersion` hoặc xóa cache qua bảng `CacheInvalidations`.

Redis sập: hệ thống rơi về DB (chậm hơn nhưng vẫn chạy), health check báo `Degraded`.

## 5. Dữ liệu và giao dịch

- EF Core code-first, migration ở `Infrastructure/Persistence/Migrations/`. Development: `Database:MigrateOnStartup=true`;
  production: `false`, migration là bước deploy riêng. `DbInitializer` luôn chạy `IdentitySeeder` (danh mục quyền,
  9 vai trò hệ thống, admin đầu tiên từ `Seed:*`).
- Tên bảng PascalCase (`"Users"`, `"SessionFamilies"`, `"AuditRecords"`). Khóa `Guid.CreateVersion7()`,
  thời điểm `timestamptz` UTC.
- Transaction: handler gọi `IUnitOfWork.BeginTransactionAsync` khi cần khóa hàng `FOR UPDATE`
  (`SessionRepository`), advisory lock (`pg_advisory_xact_lock` trong `UserRepository` cho bất biến "còn ≥ 1 admin"),
  hoặc commit trước khi trả lỗi.
- Việc sau commit: `ICacheInvalidator` ghi hàng `CacheInvalidations` trong cùng transaction → `FlushAsync` xóa key Redis
  sau commit → `CacheInvalidationWorker` retry hàng còn sót. Module nghiệp vụ dùng Outbox theo đặc tả §11.3.
- Audit: `AuditLogs` (diff dữ liệu, ghi tự động bởi `AuditSaveChangesInterceptor` cho entity `IAuditable`) và
  `AuditRecords` (truy cập/bảo mật: đăng nhập, reuse, 403, đổi quyền, đọc dữ liệu nhạy cảm qua `IAuditedRequest`).

## 6. Lỗi, log, sức khỏe

- Mọi lỗi → Problem Details (RFC 9457) có `code` (`ErrorCodes`), `traceId`, `errors`; message tiếng Việt.
  Map tại `API/Errors/GlobalExceptionHandler.cs`.
- `CorrelationIdMiddleware` ở cả Gateway và API; Serilog enrich `CorrelationId`, `UserId`, `SessionFamilyId`.
  `SensitiveDataDestructuringPolicy` che mật khẩu/token trong log.
- `GET /health` ở API (PostgreSQL; Redis lỗi ⇒ `Degraded`) và Gateway (Redis lỗi ⇒ `Degraded`).

## 7. Frontend

```
react-codebase/src/
├── index.js · reducer.js         store, router (ConnectedRouter + history dùng chung)
├── service/http.js               axios: Bearer từ RAM, header CSRF, refresh một lần khi 401
├── feature/Auth/                 Login, ChangePassword, PrivateRoute, Can, session/ (token store,
│                                 điều phối refresh đa tab bằng Web Locks/BroadcastChannel), redux/, api/
├── feature/Loading/
└── scss/                         ITCSS + BEM
```

## 8. Hướng mở rộng đã chốt

- Thêm module: vertical slice ở Application + controller `api/v1/...` + quyền mới trong `Permissions.cs`
  (seeder đồng bộ lúc khởi động) + route Gateway + migration.
- MVP chạy một API + một worker; code vẫn phải đúng khi nhiều instance (khóa ở DB, trạng thái dùng chung ở Redis/DB,
  không khóa trong bộ nhớ).
- Object storage cho tệp: `IFileStorage` ở Application, adapter tham chiếu Azure Blob (nhà cung cấp cuối cùng: `OPEN-01`).

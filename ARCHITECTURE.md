# Kiến trúc — Hệ thống quản lý khám chữa bệnh (HMS)

Tài liệu mô tả kiến trúc **đích** của repo và trạng thái chuyển đổi hiện tại (§9). Quyết định gốc nằm trong
`Dac_ta_ky_thuat_v3.1.docx`; chỗ lệch có chủ đích được ghi trong spec theo chủ đề
(`benh_vien_be/docs/superpowers/plans/<chủ-đề>/spec.md`).

## 1. Thành phần triển khai

```
Trình duyệt ── React (:9000) ──► Gateway YARP (:5100) ──► QuanLyBenhVien.API (:5289) ──► PostgreSQL 17 (:5433)
                 access token      │ rate limit theo IP        │ validate lại JWT               ▲
                 trong RAM         │ validate JWT HS256        │ quyền endpoint (Redis → DB)    │
                 cookie __Host-rt  │ phiên session:{fid}       │ phát token, rotation, audit    │
                 cookie __Host-csrf│   Redis → miss → hỏi API  │ CacheInvalidationWorker ───────┘
                                   └──────── Redis 7.4 (:6379) ┘
                                   Serilog → Console · File (CompactJson) · Seq (:5341)
```

| Thành phần | Công nghệ | Cổng dev | Ghi chú |
|---|---|---|---|
| Frontend | React 16, Redux, redux-observable, axios, Jest | 9000 | Node 24; cùng origin với Gateway qua proxy dev |
| Gateway | ASP.NET Core 10 + YARP 2.3 | 5100 / 7100 | Điểm vào công khai duy nhất; không tham chiếu project nào |
| API | ASP.NET Core 10 (Minimal API + Carter), MediatR 12.5, FluentValidation, EF Core 10 | 5289 / 7163 | Monolith chia module; Swagger ở `/` khi Development |
| PostgreSQL | 17-alpine (Docker) | 5433 → 5432 | Nguồn sự thật nghiệp vụ. Cổng host 5433 vì máy dev có PostgreSQL cài sẵn ở 5432 |
| Redis | 7.4-alpine | 6379 | Cache phiên/quyền, rate limit. Không phải nguồn sự thật |
| Seq | 2024.3 | 5341 | Xem log có cấu trúc khi dev |

`run.ps1` ở thư mục gốc khởi động tất cả. Docker Compose nằm ở `benh_vien_be/docker-compose.yml`
(project name `hospital-management`, container `hospital-*`). Solution: `benh_vien_be/benh_vien_be.sln`.

## 2. Project backend và chiều phụ thuộc

```
                         ┌──────────────────────── QuanLyBenhVien.API (host / composition root) ────────────────────────┐
                         │ Program.cs · Composition/ · Middleware/ · Health/ · Security/                                │
                         └──────┬──────────────────────┬──────────────────────┬──────────────────────┬──────────────────┘
                                ▼                      ▼                      ▼                      ▼
                   QuanLyBenhVien.Presentation  QuanLyBenhVien.Infrastructure  QuanLyBenhVien.Persistence   (Application)
                   endpoint Carter, request DTO  Redis, JWT, hash, CSRF,        EF Core + Npgsql, DbContext,
                   adapter HTTP của endpoint     worker, health check           repository, read service,
                                │                        │                      migration, seed
                                └──────────┬─────────────┴──────────────┬───────┘
                                           ▼                            │
                                QuanLyBenhVien.Application ◄────────────┘
                                Features/, Common/, Behaviors/
                                           ▼
                                QuanLyBenhVien.Domain  (chỉ BCL)

QuanLyBenhVien.Gateway   độc lập — không tham chiếu project nào (YARP, Redis)
```

| Project | Tham chiếu | Chứa | Không được chứa |
|---|---|---|---|
| `Domain` | không gì | entity/aggregate, value object, domain event, interface repository, `IUnitOfWork`, exception miền, `Identity/Permissions.cs` | package bất kỳ, attribute persistence |
| `Application` | Domain (+ MediatR, FluentValidation, Logging.Abstractions) | use case theo `Features/<Feature>/<UseCase>/`, port (interface) cho hạ tầng, `Result`/`Error`, pipeline behavior | DbContext, `Microsoft.AspNetCore.*`, Redis, SDK ngoài |
| `Persistence` | Domain, Application (+ EF Core, Npgsql) | `AppDbContext`, `Configurations/<Module>/`, `Repositories/<Module>/`, `ReadServices/<Module>/`, `Interceptors/`, `Seed/`, `Migrations/`, `DesignTimeDbContextFactory` | Redis, HTTP, quy tắc nghiệp vụ |
| `Infrastructure` | Domain, Application | adapter không phải CSDL: Redis (`Caching/`), bảo mật (`Security/`: JWT, hash mật khẩu, refresh token, CSRF), worker nền, health check | DbContext/EF (cần DB thì qua port ở Application do Persistence implement) |
| `Presentation` | Application (+ `Microsoft.AspNetCore.App`, Carter) | `Endpoints/V{n}/<Feature>/` (module `ICarterModule` + request DTO), map `Result` → `IResult`/Problem Details, filter/extension gắn vào endpoint (quyền, CSRF), ghi cookie auth | truy vấn EF, gọi Infrastructure, quy tắc nghiệp vụ |
| `API` | Application, Persistence, Infrastructure, Presentation | host: `Program.cs`, `Composition/` (chỉ đăng ký DI), `Middleware/` (correlation id, log context, exception handler), `Health/`, `Security/` (JWT bearer, policy mặc định, `ICurrentUser`/`IRequestContext`) | logic nghiệp vụ, endpoint |
| `Gateway` | không project nào | định tuyến YARP, validate JWT + phiên, rate limit IP, xóa header nội bộ | phân quyền nghiệp vụ |

Nguyên tắc: phụ thuộc chỉ hướng vào trong; Persistence, Infrastructure và Presentation **không** tham chiếu lẫn nhau —
chúng chỉ gặp nhau ở API (composition root).

## 3. Luồng một request

```
HTTP ─► Middleware (API) ─► Endpoint Carter (Presentation)
            │                   bind request DTO → tạo Command/Query → ISender.Send(…, ct)
            │                                    ▼
            │              Pipeline MediatR: LoggingBehavior → ValidationBehavior → AuditBehavior (chỉ IAuditedRequest)
            │                                    ▼
            │              Handler (Application)  ── Command: repository (Domain) + IUnitOfWork ──► Persistence
            │                                     └─ Query:   I<Feature>ReadService (Application) ──► Persistence
            │                                    ▼
            │              Result / Result<T>  ─► Presentation map sang 200/201/204 hoặc Problem Details
            └─ exception bất ngờ / ValidationException / exception miền ─► exception handler (API) ─► Problem Details
```

- **Command** (`ICommand<TResponse>`) ghi dữ liệu qua aggregate + repository; **Query** (`IQuery<TResponse>`) đọc qua
  read service trả DTO/projection (`AsNoTracking`), không đi qua aggregate.
- **Lỗi nghiệp vụ dự kiến** trả `Result.Failure(<Feature>Errors.X)`; `Error` mang mã ổn định snake_case, message tiếng Việt
  và loại lỗi để Presentation chọn status (400/401/403/404/409/412/429). Exception chỉ dành cho lỗi bất ngờ, vi phạm bất biến
  Domain và `ValidationException` do `ValidationBehavior` ném.
- Không có behavior transaction: handler tự quản transaction qua `IUnitOfWork` (xem §6).

## 4. Module

| Module (đặc tả §1.3) | Trạng thái | Mã nghiệp vụ |
|---|---|---|
| IdentityAccess — đăng nhập, phiên, người dùng, vai trò, quyền | Đã có ở kiến trúc cũ; **đang chuyển** sang khung mới (§9). FE login/đổi mật khẩu đã có | NV-03..05 |
| PatientRegistry — hồ sơ người bệnh, định danh | Chưa làm | NV-06..09 |
| ReceptionQueue — buổi khám, cấp số, gọi lượt | Chưa làm | NV-10..12 |
| Clinical — khám, chỉ định, kết quả, bệnh án, đơn thuốc | Chưa làm | NV-13..25 |
| Inpatient — nhập viện, giường, y lệnh, xuất viện | Chưa làm | NV-26..30 |
| Billing — hóa đơn, tạm ứng, thu tiền, quyết toán | Chưa làm | NV-31..36 |
| Documents — tệp bệnh án/CLS trên object storage | Chưa làm | NV-37..41 |
| Catalog, AuditNotifications, báo cáo | Chưa làm | NV-42..43 |

Cách đặt tên theo từng project: Application chia theo nhóm use case (`Features/Auth`, `Features/Users`, `Features/Roles`…),
Persistence chia theo module dữ liệu (`Identity/`, `Common/`, sau này `Patients/`, `Billing/`…), Presentation chia theo
tài nguyên HTTP (`Endpoints/V1/Auth`…).

**Không có module đặt lịch** (đặc tả v3.x). Route Gateway `/api/appointments/**` và các route nghiệp vụ không có
`/v1` (`/api/patients/**`, `/api/doctors/**`, `/api/medical-records/**`, `/api/billing/**`) là cấu hình cũ — khi làm
module nào thì đổi route của module đó sang `/api/v1/<tài-nguyên>` theo §12.1.

## 5. Luồng xác thực và phân quyền

Chi tiết: `benh_vien_be/docs/superpowers/plans/2026-09-23-auth-permission-redesign/spec.md`.

1. `POST /api/v1/auth/login` → kiểm tra email/mật khẩu (rate limit 5 lần sai/15 phút theo email), tạo
   `SessionFamily` + `RefreshToken` (DB chỉ lưu SHA-256), trả access token JWT HS256 15 phút + cookie `__Host-rt`
   (HttpOnly, Secure, SameSite=Strict) + cookie CSRF.
2. Mỗi request: Gateway validate JWT, đọc `session:{familyId}` trong Redis; miss/lỗi → `POST /internal/sessions/validate`
   (bảo vệ bằng header `X-Internal-Key`). API validate lại chữ ký JWT.
3. Quyền hành động khai trên endpoint (Presentation) theo hằng trong `Domain/Identity/Permissions.cs`; kiểm tra đọc
   `perm:{userId}` trong Redis (không TTL), miss → tính từ DB (role + quyền lẻ).
4. `POST /api/v1/auth/refresh`: rotation nguyên tử, **strict reuse** — trình lại token đã dùng ⇒ thu hồi cả family.
   Phiên tuyệt đối 7 ngày.
5. Đổi mật khẩu/khóa tài khoản/đổi quyền: tăng `SecurityVersion` hoặc xóa cache qua bảng `CacheInvalidations`.

Redis sập: hệ thống rơi về DB (chậm hơn nhưng vẫn chạy), health check báo `Degraded`.

## 6. Dữ liệu và giao dịch

- EF Core code-first trong `QuanLyBenhVien.Persistence`, migration ở `Persistence/Migrations/`. Development:
  `Database:MigrateOnStartup=true`; production: `false`, migration là bước deploy riêng. Seeder (`Persistence/Seed/`) đồng bộ
  danh mục quyền, 9 vai trò hệ thống, admin đầu tiên từ `Seed:*`.
- Tên bảng PascalCase (`"Users"`, `"SessionFamilies"`, `"AuditRecords"`). Khóa `Guid.CreateVersion7()`,
  thời điểm `timestamptz` UTC.
- Transaction: handler gọi `IUnitOfWork.BeginTransactionAsync` khi cần khóa hàng `FOR UPDATE`
  (`SessionRepository`), advisory lock (`pg_advisory_xact_lock` cho bất biến "còn ≥ 1 admin"),
  hoặc phải commit trước khi trả `Result.Failure` (ví dụ thu hồi family khi phát hiện reuse).
- Việc sau commit: `ICacheInvalidator` ghi hàng `CacheInvalidations` trong cùng transaction → `FlushAsync` xóa key Redis
  sau commit → `CacheInvalidationWorker` (Infrastructure) retry hàng còn sót, đọc/ghi bảng qua port do Persistence implement.
  Module nghiệp vụ dùng Outbox theo đặc tả §11.3.
- Audit: `AuditLogs` (diff dữ liệu, ghi tự động bởi `AuditSaveChangesInterceptor` cho entity `IAuditable`) và
  `AuditRecords` (truy cập/bảo mật: đăng nhập, reuse, 403, đổi quyền, đọc dữ liệu nhạy cảm qua `IAuditedRequest`),
  ghi qua port `Application/Common/Auditing/IAuditWriter`.

## 7. Lỗi, log, sức khỏe

- Mọi lỗi → Problem Details (RFC 9457) có `code`, `traceId`, `errors`; message tiếng Việt. Hai nguồn, **cùng một định dạng**:
  `Result.Failure(Error)` map ở Presentation; exception (validation, miền, bất ngờ) map ở exception handler của API.
- `CorrelationIdMiddleware` ở cả Gateway và API; Serilog enrich `CorrelationId`, `UserId`, `SessionFamilyId`.
  `SensitiveDataDestructuringPolicy` che mật khẩu/token trong log.
- `GET /health` ở API (PostgreSQL; Redis lỗi ⇒ `Degraded`) và Gateway (Redis lỗi ⇒ `Degraded`).

## 8. Frontend

```
benh_vien_fe/src/
├── index.js · reducer.js         store, router (ConnectedRouter + history dùng chung)
├── service/http.js               axios: Bearer từ RAM, header CSRF, refresh một lần khi 401
├── feature/Auth/                 Login, ChangePassword, PrivateRoute, Can, session/ (token store,
│                                 điều phối refresh đa tab bằng Web Locks/BroadcastChannel), redux/, api/
├── feature/Loading/
└── scss/                         ITCSS + BEM
```

## 9. Trạng thái chuyển đổi (2026-09-28)

Backend đang chuyển từ khung cũ (Controllers, `Application/<Feature>/{Commands,Queries}`, exception cho lỗi nghiệp vụ,
EF nằm trong Infrastructure) sang khung ở §2–§3. Khi đọc code, phân biệt:

| Phần | Trạng thái |
|---|---|
| `Domain` | Giữ nguyên từ bản cũ |
| `Application` | Khung mới: `Features/Auth/<UseCase>/`, `Common/{Messaging,Results,…}` — phần lớn là stub (`NotImplementedException`, interface rỗng). `AuditBehavior`/`IAuditedRequest` chưa có |
| `Persistence` | Khung mới, class còn rỗng; `Migrations/` trống |
| `Presentation` | `Endpoints/V1/Auth/AuthEndpoints.cs` khai route, handler còn stub; chưa có map `Result` → Problem Details, filter quyền/CSRF |
| `Infrastructure` | Còn **mã cũ** (`Persistence/`, `Repositories/`, `Identity/`, `DependencyInjection/`) và migration cũ ở `Infrastructure/Persistence/Migrations/` — chờ chuyển/xóa |
| `API` | `Program.cs` còn bản cũ (`AddControllers`, `MapControllers`); `Composition/`, `Health/`, `Middleware/`, `Security/` mới có README/thư mục |
| `tests/` | Viết cho khung cũ; phải cập nhật theo khung mới |

Lưu ý khi chuyển migration: DB dev đã áp dụng migration cũ. Chuyển file sang `Persistence/Migrations/` phải giữ nguyên
`MigrationId` (tên lớp/attribute `[Migration("…")]`) để EF không áp lại; tạo migration "khởi đầu" mới thay thế là thay đổi
lịch sử — `CẦN_XÁC_NHẬN` với người dùng trước khi làm.

## 10. Hướng mở rộng đã chốt

- Thêm module: Domain (aggregate + repository interface + quyền trong `Permissions.cs`) → Application
  (`Features/<Feature>/<UseCase>/`) → Persistence (configuration, repository, read service, migration) → Presentation
  (`Endpoints/V1/<Feature>/`) → route Gateway → frontend.
- Endpoint V2 chỉ tạo khi hợp đồng HTTP thật sự cần phiên bản mới; V2 có thể gọi cùng use case với V1.
- MVP chạy một API + một worker; code vẫn phải đúng khi nhiều instance (khóa ở DB, trạng thái dùng chung ở Redis/DB,
  không khóa trong bộ nhớ). Worker nặng (xử lý tệp, quét) nên là host riêng.
- Object storage cho tệp: `IFileStorage` ở Application, adapter ở Infrastructure (tham chiếu Azure Blob; nhà cung cấp cuối
  cùng: `OPEN-01`).

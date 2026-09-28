# benh_vien_be — Backend HMS

Backend của Hệ thống quản lý khám chữa bệnh: .NET 10, Clean Architecture + CQRS (MediatR 12.5), Minimal API + Carter,
EF Core 10 + PostgreSQL 17, Redis 7.4, Gateway YARP.

Tài liệu chung ở thư mục gốc repo: cách chạy và secret trong `README.md`, kiến trúc và **trạng thái chuyển khung** trong
`ARCHITECTURE.md` (§9), bảo mật trong `SECURITY.md`, lệnh kiểm tra trong `VALIDATION.md`. File này chỉ tóm tắt quy ước
theo từng project.

## Project và chiều phụ thuộc

```
Domain          ← không tham chiếu gì (chỉ BCL)
Application     → Domain
Persistence     → Domain, Application
Infrastructure  → Domain, Application
Presentation    → Application
API             → Application, Persistence, Infrastructure, Presentation   (host / composition root)
Gateway         → không tham chiếu project nào
```

Persistence, Infrastructure và Presentation không tham chiếu lẫn nhau; chúng chỉ được ghép ở API.

## Cấu trúc

```
benh_vien_be/
├── benh_vien_be.sln
├── docker-compose.yml                         PostgreSQL 17 (:5433), Redis 7.4 (:6379), Seq (:5341)
├── src/
│   ├── QuanLyBenhVien.Domain/
│   │   ├── Common/                            Entity<TId>, AggregateRoot<TId>, IUnitOfWork, Auditing/
│   │   ├── Exceptions/                        DomainException, BusinessRuleViolationException, NotFoundException
│   │   └── Identity/                          User, Role, Permission, Permissions.cs, Sessions/, Events/
│   ├── QuanLyBenhVien.Application/
│   │   ├── Behaviors/                         LoggingBehavior, ValidationBehavior (AuditBehavior sẽ thêm)
│   │   ├── Common/                            Messaging (ICommand/IQuery), Results (Result/Error), Models (PagedResult),
│   │   │                                      Identity, Caching, Auditing, Security — port cho hạ tầng
│   │   ├── Features/<Feature>/
│   │   │   ├── Common/                        <Feature>Errors, port riêng của feature, DTO dùng chung
│   │   │   └── <UseCase>/                     XxxCommand|Query, handler, validator, DTO
│   │   └── DependencyInjection.cs
│   ├── QuanLyBenhVien.Persistence/
│   │   ├── AppDbContext.cs · AppDbTransaction.cs · DesignTimeDbContextFactory.cs · DependencyInjection.cs
│   │   ├── Configurations/<Module>/           Fluent API
│   │   ├── Repositories/<Module>/             cho command
│   │   ├── ReadServices/<Module>/             cho query (AsNoTracking + projection)
│   │   ├── Interceptors/ · Seed/ · Migrations/
│   ├── QuanLyBenhVien.Infrastructure/         Caching/ (Redis), Security/ (JWT, hash, CSRF), worker, HealthChecks/
│   ├── QuanLyBenhVien.Presentation/
│   │   └── Endpoints/V1/<Feature>/            <Feature>Endpoints.cs (ICarterModule) + XxxRequest.cs
│   ├── QuanLyBenhVien.API/                    Program.cs, Composition/, Middleware/, Health/, Security/
│   └── QuanLyBenhVien.Gateway/                YARP — xem src/QuanLyBenhVien.Gateway/README.md
├── tests/
│   ├── QuanLyBenhVien.UnitTests/              mirror src theo project
│   └── QuanLyBenhVien.IntegrationTests/       Testcontainers PostgreSQL + Redis, ApiFactory
├── docs/superpowers/plans/<chủ-đề>/           spec.md + plan*.md đã duyệt
└── .sdd/Plan/                                 plan gốc theo phase (một phần đã bị spec mới thay thế)
```

## Một use case đi qua các project

Ví dụ đăng nhập (`POST /api/v1/auth/login`):

| Project | File |
|---|---|
| Presentation | `Endpoints/V1/Auth/AuthEndpoints.cs` (route) · `Endpoints/V1/Auth/LoginRequest.cs` |
| Application | `Features/Auth/Login/{LoginCommand, LoginCommandHandler, LoginCommandValidator}.cs` · `Features/Auth/Common/{AuthErrors, AuthTokensResult, ILoginAttemptLimiter, IAccessTokenIssuer, …}.cs` |
| Domain | `Identity/User.cs` · `Identity/Sessions/SessionFamily.cs` · `IUserRepository`, `ISessionRepository` |
| Persistence | `Repositories/Identity/{UserRepository, SessionRepository}.cs` · `Configurations/Identity/*` |
| Infrastructure | JWT, hash mật khẩu, rate limit đăng nhập (Redis) — implement port ở `Features/Auth/Common/` |
| API | `Composition/` đăng ký DI của các project trên |

Quy ước chính:

- Command/Query là `sealed record` implement `ICommand<Result…>`/`IQuery<Result<T>>`; handler và validator `internal sealed`.
- Lỗi nghiệp vụ dự kiến trả `Result.Failure(<Feature>Errors.X)`; Presentation map sang Problem Details (`code`, `traceId`, `errors`).
- Handler tự quản transaction qua `IUnitOfWork` khi cần khóa hàng/advisory lock/nhiều bước; không gọi Redis/HTTP trong transaction.
- Endpoint mặc định cần đăng nhập; quyền khai trên route theo `Domain/Identity/Permissions.cs`.
- Route mới phải thêm vào `src/QuanLyBenhVien.Gateway/appsettings.json`.

Quy tắc đầy đủ: `AGENTS.md` và `.claude/rules/*.md` ở thư mục gốc repo.

## Lệnh thường dùng

Chạy từ `benh_vien_be/`:

```powershell
docker compose up -d --wait
dotnet build benh_vien_be.sln
dotnet run --project src/QuanLyBenhVien.API --launch-profile http        # :5289, Swagger ở / khi Development
dotnet run --project src/QuanLyBenhVien.Gateway --launch-profile http    # :5100
dotnet test tests/QuanLyBenhVien.UnitTests
dotnet test tests/QuanLyBenhVien.IntegrationTests                        # cần Docker

dotnet tool restore
dotnet ef migrations add <TenMigration> --project src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API --output-dir Migrations
```

Secret (`Jwt:SigningKey`, `Auth:CsrfKey`, `Auth:InternalApiKey`, `Seed:AdminPassword`) đặt bằng `dotnet user-secrets` —
xem mục "Cấu hình secret lần đầu" trong `README.md` ở thư mục gốc.

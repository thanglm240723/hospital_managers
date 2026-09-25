# HMS — Hệ thống quản lý khám chữa bệnh

Hệ thống quản lý khám chữa bệnh ngoại trú và nội trú: tiếp nhận và lấy số, hàng chờ, khám và chỉ định cận lâm sàng,
bệnh án và kết quả có phiên bản, đơn thuốc, nội trú và giường, viện phí, tệp bệnh án trên cloud.
Không có chức năng đặt lịch hẹn: bệnh viện chỉ tiếp nhận và cấp số trực tiếp.

| | |
|---|---|
| Backend | .NET 10, ASP.NET Core, Clean Architecture + CQRS (MediatR 12.5), EF Core 10 + Npgsql |
| Gateway | YARP 2.3: xác thực JWT, kiểm tra phiên, rate limit |
| Cơ sở dữ liệu | PostgreSQL 17 (nguồn sự thật), Redis 7.4 (cache phiên/quyền) |
| Frontend | React 16, Redux, redux-observable, axios, Jest (Node 24) |
| Log | Serilog → Console, file JSON, Seq |
| Test | xUnit, Testcontainers (PostgreSQL + Redis thật), Jest |

**Trạng thái:** đã xong module IdentityAccess (đăng nhập, phiên, người dùng, vai trò, quyền, audit) ở cả backend,
Gateway và màn hình đăng nhập/đổi mật khẩu. Các module nghiệp vụ còn lại chưa làm, xem [ARCHITECTURE.md](ARCHITECTURE.md) §3.

## Mục lục

- [Cấu trúc repo](#cấu-trúc-repo)
- [Yêu cầu cài đặt](#yêu-cầu-cài-đặt)
- [Chạy nhanh](#chạy-nhanh)
- [Cấu hình secret lần đầu](#cấu-hình-secret-lần-đầu)
- [Chạy từng thành phần](#chạy-từng-thành-phần)
- [Test](#test)
- [Database và migration](#database-và-migration)
- [Tài liệu](#tài-liệu)
- [Agent và MCP](#agent-và-mcp)
- [Xử lý sự cố](#xử-lý-sự-cố)

## Cấu trúc repo

```
hospital_management/
├── Dac_ta_nghiep_vu_v2.0.docx           Đặc tả nghiệp vụ (BRD): NV-xx, CFG-xx, AT-xx, OPEN-xx
├── Dac_ta_ky_thuat_v3.1.docx            Đặc tả kỹ thuật (TSD), dùng PostgreSQL
├── ARCHITECTURE.md                      Kiến trúc đang chạy
├── SECURITY.md                          Baseline bảo mật
├── VALIDATION.md                        Cách kiểm tra và trạng thái test
├── AGENTS.md · CLAUDE.md                Hướng dẫn cho coding agent (Codex, Claude Code)
├── run.ps1                              Chạy toàn bộ hệ thống bằng một lệnh
├── tooling/validate.ps1                 Build + test, in tóm tắt
├── .claude/                             Rule, skill, subagent, hook cho Claude Code
├── .codex/  .config/                    Cấu hình Codex; tool .NET cục bộ (dotnet-ef)
├── dotnet-clean-architecture-cqrs-starter/
│   ├── docker-compose.yml               PostgreSQL, Redis, Seq
│   ├── src/
│   │   ├── CleanArchCqrs.Domain/        Entity, quy tắc nghiệp vụ, interface repository
│   │   ├── CleanArchCqrs.Application/   Command/Query, validator, pipeline behavior
│   │   ├── CleanArchCqrs.Infrastructure/ EF Core, migration, repository, Redis, bảo mật
│   │   ├── CleanArchCqrs.API/           Controller, xác thực, phân quyền, xử lý lỗi
│   │   └── CleanArchCqrs.Gateway/       YARP gateway
│   ├── tests/
│   │   ├── CleanArchCqrs.UnitTests/
│   │   └── CleanArchCqrs.IntegrationTests/
│   ├── docs/superpowers/plans/          Spec và plan theo chủ đề (đã duyệt)
│   └── .sdd/Plan/                       Plan gốc theo phase
└── react-codebase/                      Frontend
```

## Yêu cầu cài đặt

- Windows 10/11 với PowerShell 5.1+ (script dùng Windows PowerShell)
- [.NET SDK 10](https://dotnet.microsoft.com/download/dotnet/10.0) (đã kiểm tra với 10.0.400)
- [Node.js 24](https://nodejs.org/) và npm
- [Docker Desktop](https://www.docker.com/products/docker-desktop/): chạy PostgreSQL, Redis, Seq, và cần cho integration test

## Chạy nhanh

```powershell
# 1. Chỉ làm lần đầu: cấu hình secret (xem mục bên dưới)
# 2. Chạy toàn bộ hệ thống
.\run.ps1
```

`run.ps1` sẽ:
1. Mở Docker Desktop nếu chưa chạy, rồi khởi động PostgreSQL, Redis và Seq (`docker compose up -d --wait`).
2. Build API và Gateway, mỗi thành phần chạy trong một cửa sổ PowerShell riêng (đóng cửa sổ là dừng).
3. `npm install` nếu chưa có `node_modules`, rồi chạy frontend.

Thành phần nào đang chạy sẵn (cổng đã mở) thì script bỏ qua.

| Địa chỉ | Dùng để |
|---|---|
| http://localhost:9000 | Frontend: mở trang này để dùng hệ thống |
| http://localhost:5100 | Gateway: điểm vào API (frontend dev server proxy `/api` tới đây) |
| http://localhost:5289 | API: Swagger UI ở `/` (chỉ Development) |
| http://localhost:5341 | Seq: xem log |
| `localhost:5433` | PostgreSQL (user/pass `postgres`/`postgres`, DB `CleanArchCqrsDb`) |
| `localhost:6379` | Redis |

**Đăng nhập lần đầu:** email `admin@hospital.local` (đặt trong `appsettings.Development.json`), mật khẩu là giá trị
`Seed:AdminPassword` bạn đặt ở bước secret. Hệ thống bắt đổi mật khẩu ngay lần đầu. Mật khẩu mới dài 10–128 ký tự
và không được chứa phần tên trước `@` của email.

Dừng hạ tầng Docker (giữ nguyên dữ liệu):

```powershell
docker compose -f dotnet-clean-architecture-cqrs-starter\docker-compose.yml stop
```

## Cấu hình secret lần đầu

Secret **không** nằm trong `appsettings*.json`. Khi dev, đặt secret bằng `dotnet user-secrets`. API và Gateway kiểm tra
secret lúc khởi động: thiếu hoặc quá ngắn thì báo lỗi ngay.

```powershell
cd dotnet-clean-architecture-cqrs-starter

# Tự sinh chuỗi ngẫu nhiên 48 ký tự
function New-Secret { -join ((48..57) + (65..90) + (97..122) | Get-Random -Count 48 | ForEach-Object { [char]$_ }) }
$jwt = New-Secret; $csrf = New-Secret; $internal = New-Secret

# API
dotnet user-secrets set "Jwt:SigningKey"      $jwt      --project src/CleanArchCqrs.API
dotnet user-secrets set "Auth:CsrfKey"        $csrf     --project src/CleanArchCqrs.API
dotnet user-secrets set "Auth:InternalApiKey" $internal --project src/CleanArchCqrs.API
dotnet user-secrets set "Seed:AdminPassword"  "<mật khẩu admin ban đầu>" --project src/CleanArchCqrs.API

# Gateway: SigningKey giống API, InternalApiKey giống Auth:InternalApiKey của API
dotnet user-secrets set "Jwt:SigningKey"          $jwt      --project src/CleanArchCqrs.Gateway
dotnet user-secrets set "Identity:InternalApiKey" $internal --project src/CleanArchCqrs.Gateway
```

| Khóa | Nơi đặt | Yêu cầu |
|---|---|---|
| `Jwt:SigningKey` | API + Gateway (cùng giá trị) | ≥ 32 ký tự |
| `Auth:CsrfKey` | API | ≥ 32 ký tự |
| `Auth:InternalApiKey` / `Identity:InternalApiKey` | API / Gateway (cùng giá trị) | không rỗng |
| `Seed:AdminPassword` | API | chỉ dùng khi DB chưa có admin |

Production dùng secret store hoặc biến môi trường. Xem [SECURITY.md](SECURITY.md).

## Chạy từng thành phần

```powershell
cd dotnet-clean-architecture-cqrs-starter
docker compose up -d --wait                                                    # PostgreSQL, Redis, Seq
dotnet run --project src/CleanArchCqrs.API --launch-profile http              # :5289
dotnet run --project src/CleanArchCqrs.Gateway --launch-profile http          # :5100

cd ..\react-codebase
npm install
npm start                                                                      # :9000
```

Ở Development, API tự áp migration khi khởi động (`Database:MigrateOnStartup=true`) và seed danh mục quyền,
9 vai trò hệ thống và tài khoản admin đầu tiên.

## Test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Quick      # build + unit test
powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Full       # + integration test (cần Docker)
powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Frontend   # Jest + ESLint
powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode All
```

Log đầy đủ nằm ở `.claude/work/logs/`. Integration test tự dựng PostgreSQL 17 và Redis bằng Testcontainers,
không đụng tới DB dev. Lệnh thủ công, cách chọn mức kiểm tra và trạng thái mới nhất: [VALIDATION.md](VALIDATION.md).

## Database và migration

- EF Core **code-first**. Migration nằm ở `src/CleanArchCqrs.Infrastructure/Persistence/Migrations/`.
- Tool `dotnet-ef` được ghim phiên bản trong `.config/dotnet-tools.json`. Chạy `dotnet tool restore` một lần.

```powershell
cd dotnet-clean-architecture-cqrs-starter
dotnet ef migrations add <TenMigration> --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API --output-dir Persistence/Migrations
dotnet ef migrations script --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API
```

- Không sửa migration đã áp dụng. Muốn sửa thì tạo migration mới.
- Production: `Database:MigrateOnStartup=false`, migration chạy như một bước deploy riêng. Thay đổi phá tương thích
  làm theo expand → deploy → backfill → contract.
- PostgreSQL trong Docker dùng cổng host **5433**, vì máy dev có thể đã có PostgreSQL cài sẵn ở cổng 5432.

## Tài liệu

| Tài liệu | Nội dung |
|---|---|
| `Dac_ta_nghiep_vu_v2.0.docx` | Yêu cầu nghiệp vụ, tiêu chí nghiệm thu, mục còn mở (OPEN) |
| `Dac_ta_ky_thuat_v3.1.docx` | Kiến trúc, dữ liệu, phân quyền, giao dịch, API, vận hành. Bản 3.1 chuyển từ SQL Server sang PostgreSQL |
| [ARCHITECTURE.md](ARCHITECTURE.md) | Thành phần, layer, module, luồng xác thực, dữ liệu và giao dịch |
| [SECURITY.md](SECURITY.md) | Secret, xác thực, phân quyền, log, tệp |
| [VALIDATION.md](VALIDATION.md) | Lệnh kiểm tra và kết quả gần nhất |
| `dotnet-clean-architecture-cqrs-starter/docs/superpowers/plans/` | Spec và plan theo chủ đề, ví dụ `2026-09-23-auth-permission-redesign/` |
| `dotnet-clean-architecture-cqrs-starter/.sdd/Plan/00-quyet-dinh-va-quy-uoc.md` | Quy ước đặt tên và cấu trúc code |
| `dotnet-clean-architecture-cqrs-starter/src/CleanArchCqrs.Gateway/README.md` | Bảng định tuyến Gateway |

Nếu các nguồn mâu thuẫn, thứ tự ưu tiên là: đặc tả → spec theo chủ đề → quy ước `.sdd` → code. Chi tiết xem [AGENTS.md](AGENTS.md).

## Agent và MCP

Repo đã cấu hình sẵn cho coding agent:

- **Codex** đọc `AGENTS.md` và `.codex/config.toml`.
- **Claude Code** đọc `CLAUDE.md` (file này import `AGENTS.md`) và thư mục `.claude/`:
  - `rules/`: quy tắc tự nạp theo đường dẫn file (Domain, Application, Persistence, API, Gateway, frontend, test, bất biến nghiệp vụ).
  - `skills/`: `/feature`, `/complex-plan`, `/review-diff`, `/ef-migration`, `/long-task`, cùng `transaction-write`,
    `postgres-concurrency`, `api-contract-change`, `object-storage`, `react-feature`.
  - `agents/`: `architecture-planner`, `reviewer` (opus); `dotnet-implementer`, `react-implementer`, `debugger`,
    `database-analyst`, `platform-reviewer` (sonnet); `mechanical-editor` (haiku).
  - `settings.json` và `hooks/guard-shell.ps1`: chặn đọc file secret và chặn lệnh nguy hiểm. Hook chạy cả khi bạn
    bỏ qua hộp thoại hỏi quyền. Các lệnh bị chặn gồm `git push`, `reset --hard`, `rebase`, xóa đệ quy, DDL/DML qua
    `psql`, `dotnet ef database drop`, `docker compose down -v`.

**Cho agent đọc DB dev (chỉ đọc) qua MCP DBHub:**

```powershell
Copy-Item .claude\dbhub-dev.example.toml .claude\dbhub-dev.toml   # file này bị .gitignore
# Mở .claude\dbhub-dev.toml, điền mật khẩu (DB dev trong Docker: postgres)
claude mcp add hms-db -- powershell -NoProfile -ExecutionPolicy Bypass -File .claude/run-dbhub-dev.ps1
```

Script chạy DBHub `1.2.3` qua `npx`. Tool `execute_sql` được cấu hình `readonly = true`.

## Xử lý sự cố

| Triệu chứng | Nguyên nhân và cách xử lý |
|---|---|
| API/Gateway không khởi động, báo `Jwt:SigningKey must be at least 32 characters` | Chưa đặt user-secrets. Xem [Cấu hình secret lần đầu](#cấu-hình-secret-lần-đầu) |
| Build lỗi `MSB3027 … file is locked by CleanArchCqrs.Gateway` | App đang chạy. Đóng cửa sổ PowerShell của API/Gateway rồi build lại |
| API không kết nối được DB | Kiểm tra `docker ps` có container `hospital-postgres`, và connection string dùng cổng `5433` |
| Đăng nhập được nhưng mọi request trả 401 | `Jwt:SigningKey` hoặc `InternalApiKey` của API và Gateway không khớp nhau |
| Health báo `Degraded` | Redis không chạy. Hệ thống vẫn hoạt động (đọc từ DB), chỉ chậm hơn |
| Integration test fail ngay từ đầu | Docker Desktop chưa chạy |
| `dotnet ef` báo lỗi JSON ở `.config/dotnet-tools.json` | Chạy `dotnet tool restore` từ thư mục repo |

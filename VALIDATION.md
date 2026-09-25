# Kiểm tra (validation) — HMS

Cách kiểm tra thay đổi trước khi báo "xong", và trạng thái lần kiểm tra gần nhất.
Quy tắc: không báo PASS khi chưa có output lệnh mới chạy; không chạy được thì ghi `NOT_RUN`/`BLOCKED`.

## Script

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tooling/validate.ps1 -Mode Quick
```

| Mode | Làm gì | Yêu cầu |
|---|---|---|
| `Quick` | build cả solution + unit test | .NET SDK 10 |
| `Full` | `Quick` + integration test (Testcontainers: PostgreSQL 17 + Redis 7.4) | Docker Desktop đang chạy |
| `Frontend` | Jest (CI) + ESLint cho `react-codebase` | Node 24, đã `npm install` |
| `All` | `Full` + `Frontend` | tất cả ở trên |

Script chỉ in tóm tắt; log đầy đủ nằm ở `.claude/work/logs/validate-<thời-điểm>-<bước>.log`.
Exit code `0` khi mọi bước PASS.

⚠ Build lỗi `MSB3027 … file is locked by CleanArchCqrs.Gateway/API` nghĩa là app đang chạy (cửa sổ `run.ps1`) —
đóng cửa sổ đó rồi chạy lại; đây không phải lỗi code.

## Lệnh thủ công

Chạy từ `dotnet-clean-architecture-cqrs-starter/`:

```powershell
dotnet build dotnet-clean-architecture-cqrs-starter.sln
dotnet test tests/CleanArchCqrs.UnitTests
dotnet test tests/CleanArchCqrs.IntegrationTests                  # cần Docker
dotnet test tests/CleanArchCqrs.IntegrationTests --filter "FullyQualifiedName~Auth.LoginTests"
```

Chạy từ `react-codebase/`:

```powershell
$env:CI = 'true'; npm test        # Jest chạy một lần, không watch
npx eslint src
```

Migration (tool cục bộ, khai báo ở `.config/dotnet-tools.json`, chạy `dotnet tool restore` một lần):

```powershell
dotnet ef migrations add <TenMigration> --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API --output-dir Persistence/Migrations
dotnet ef migrations script <MigrationTruoc> <TenMigration> --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API
```

## Chọn mức kiểm tra

| Thay đổi | Tối thiểu |
|---|---|
| Domain/Application thuần, validator | `Quick` |
| Repository, cấu hình EF, migration, SQL thô, khóa/transaction, Redis, Gateway, auth | `Full` |
| Frontend | `Frontend` |
| Hợp đồng API dùng bởi frontend | `All` |
| Chỉ tài liệu/cấu hình agent | không cần build; kiểm tra cú pháp file (JSON/TOML/PowerShell) |

## Trạng thái gần nhất

| Ngày | Bước | Kết quả | Ghi chú |
|---|---|---|---|
| 2026-09-25 | unit-tests | PASS — 94/94 | `tooling/validate.ps1 -Mode Quick` |
| 2026-09-25 | build | BLOCKED | Gateway đang chạy khóa `CleanArchCqrs.Gateway.exe`; không phải lỗi biên dịch |
| 2026-09-25 | frontend-tests | PASS — 33/33, 10 suite | `-Mode Frontend` |
| 2026-09-25 | frontend-lint | PASS | |
| 2026-09-24 | integration-tests | PASS — 76/76 | theo `tests/CleanArchCqrs.IntegrationTests/TestResults/full-run.trx` (người dùng chạy) |

Cập nhật bảng này khi chạy lại kiểm tra cho một mốc quan trọng (xong một plan/module), không cần cho mỗi thay đổi nhỏ.

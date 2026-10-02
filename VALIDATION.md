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
| `Frontend` | Jest (CI) + ESLint cho `benh_vien_fe` | Node 24, đã `npm install` |
| `All` | `Full` + `Frontend` | tất cả ở trên |

Script chỉ in tóm tắt; log đầy đủ nằm ở `.claude/work/logs/validate-<thời-điểm>-<bước>.log`.
Exit code `0` khi mọi bước PASS.

⚠ Build lỗi `MSB3027 … file is locked by QuanLyBenhVien.Gateway/API` nghĩa là app đang chạy (cửa sổ `run.ps1`) —
đóng cửa sổ đó rồi chạy lại; đây không phải lỗi code.

## Lệnh thủ công

Chạy từ `benh_vien_be/`:

```powershell
dotnet build benh_vien_be.sln
dotnet test tests/QuanLyBenhVien.UnitTests
dotnet test tests/QuanLyBenhVien.IntegrationTests                  # cần Docker
dotnet test tests/QuanLyBenhVien.IntegrationTests --filter "FullyQualifiedName~Auth.LoginTests"
```

Chạy từ `benh_vien_fe/`:

```powershell
$env:CI = 'true'; npm test        # Jest chạy một lần, không watch
npx eslint src
```

Migration (tool cục bộ, khai báo ở `.config/dotnet-tools.json`, chạy `dotnet tool restore` một lần):

```powershell
dotnet ef migrations add <TenMigration> --project src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API --output-dir Migrations
dotnet ef migrations script <MigrationTruoc> <TenMigration> --project src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API
```

## Chọn mức kiểm tra

| Thay đổi | Tối thiểu |
|---|---|
| Domain/Application thuần, validator, map `Result` → HTTP | `Quick` |
| Persistence (repository, read service, cấu hình EF, migration, SQL thô), khóa/transaction, Redis, endpoint Presentation, Gateway, auth | `Full` |
| Frontend | `Frontend` |
| Hợp đồng API dùng bởi frontend | `All` |
| Chỉ tài liệu/cấu hình agent | không cần build; kiểm tra cú pháp file (JSON/TOML/PowerShell) |

## Trạng thái gần nhất

| Ngày | Bước | Kết quả | Ghi chú |
|---|---|---|---|
| 2026-10-01 | build + unit-tests | PASS — 166/166 | `-Mode Full` (plan 04 login & khu vực, task 4) |
| 2026-10-01 | integration-tests | PASS — 144 passed, 8 skipped, 152 total | `-Mode Full`, Docker/Testcontainers; đã bật PermissionService, PermissionCacheInvalidation, AuthorizationPipeline (plan 04, task 4) |
| 2026-10-01 | frontend-tests + lint | PASS — 130/130, 24 suite | `-Mode Frontend` (plan 04, task 4) |
| 2026-10-01 | frontend build | PASS — exit 0 | `npm run build` trong `benh_vien_fe` (plan 04, task 4) |
| 2026-10-01 | kiểm tay trình duyệt plan 04 (đổi mật khẩu bắt buộc, không quyền, nhiều khu vực, URL trực tiếp, đổi khu vực, đổi quyền + Kiểm tra lại) | NOT_RUN | agent không điều khiển được trình duyệt thật; người dùng tự chạy checklist trong báo cáo task 4 |
| 2026-10-01 | build + unit-tests | PASS — 158/158 | `-Mode Full` (plan 03 refresh, task 4) |
| 2026-10-01 | integration-tests | PASS — 125 passed, 8 skipped, 133 total | `-Mode Full`, Docker/Testcontainers; đã bật `LogoutRefreshInteropTests`, `PasswordChangeRefreshTests` (plan 03, task 4) |
| 2026-10-01 | frontend-tests + lint | PASS — 101/101, 22 suite | `-Mode Frontend` (plan 03, task 4) |
| 2026-10-01 | frontend build | PASS — exit 0 | `npm run build` trong `benh_vien_fe` (plan 03, task 4) |
| 2026-10-01 | kiểm tay trình duyệt (F5, nhiều tab, chờ refresh, logout nhiều tab, DevTools) | NOT_RUN | agent không điều khiển được trình duyệt thật; người dùng tự chạy checklist trong báo cáo task 4 |
| 2026-09-30 | build + unit-tests | PASS — 157/157 | `-Mode Full` (plan 02 logout, task 4) |
| 2026-09-30 | integration-tests | PASS — 94 passed, 11 skipped, 105 total | `-Mode Full`, Docker/Testcontainers (plan 02 logout, task 4) |
| 2026-09-30 | frontend-tests + lint | PASS — 83/83, 20 suite | `-Mode Frontend` (plan 02 logout, task 4) |
| 2026-09-30 | frontend build | PASS — exit 0 | `npm run build` trong `benh_vien_fe` (plan 02 logout, task 4) |
| 2026-09-25 | unit-tests | PASS — 94/94 | `tooling/validate.ps1 -Mode Quick` |
| 2026-09-25 | build | BLOCKED | Gateway đang chạy khóa `QuanLyBenhVien.Gateway.exe`; không phải lỗi biên dịch |
| 2026-09-25 | frontend-tests | PASS — 33/33, 10 suite | `-Mode Frontend` |
| 2026-09-25 | frontend-lint | PASS | |
| 2026-09-24 | integration-tests | PASS — 76/76 | theo `tests/QuanLyBenhVien.IntegrationTests/TestResults/full-run.trx` (người dùng chạy) |

Các kết quả trên đo trên **khung cũ**. Từ khi chuyển sang khung mới (`ARCHITECTURE.md` §9), solution còn stub
`NotImplementedException` và `Program.cs` cũ — chưa có lần build/test nào trên khung mới; coi trạng thái hiện tại là `NOT_RUN`.

Cập nhật bảng này khi chạy lại kiểm tra cho một mốc quan trọng (xong một plan/module), không cần cho mỗi thay đổi nhỏ.

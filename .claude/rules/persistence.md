---
paths:
  - "**/QuanLyBenhVien.Persistence/**/*.cs"
---
# Quy tắc Persistence (PostgreSQL + EF Core code-first)

`QuanLyBenhVien.Persistence` chỉ lo truy cập dữ liệu. Tham chiếu Domain + Application; không Redis, không HTTP,
không quy tắc nghiệp vụ. Mã EF cũ còn nằm ở `Infrastructure/Persistence/` — xem `ARCHITECTURE.md` §9.

## Bố cục
- `AppDbContext.cs` (implement `IUnitOfWork`), `AppDbTransaction.cs`, `DesignTimeDbContextFactory.cs`, `DependencyInjection.cs`.
- `Configurations/<Module>/XxxConfiguration.cs` — `Common/` (audit, cache invalidation), `Identity/`, module mới thêm thư mục riêng.
- `Repositories/<Module>/XxxRepository.cs` — implement interface repository ở Domain, `internal sealed`.
- `ReadServices/<Module>/XxxReadService.cs` — implement port đọc của Application (`Features/<Feature>/Common/I…ReadService`).
- `Interceptors/` (`AuditSaveChangesInterceptor`), `Seed/` (`DbInitializer`, seeder, options), `Migrations/`.

## Mapping
- Mỗi entity một `IEntityTypeConfiguration<T>`, `builder.ToTable("TenSoNhieu")`.
  Tên bảng/cột PascalCase như hiện có — không chuyển snake_case.
- Khóa `Guid` do Domain sinh (`Guid.CreateVersion7()`), cấu hình `ValueGeneratedNever()` khi cần.
- `DateTimeOffset` → `timestamptz`, chỉ lưu offset 0 (Npgsql từ chối offset khác). Ngày sinh `DateOnly` → `date`.
- Tiền `decimal` → `HasPrecision(19, 2)`; tỷ lệ `(7, 4)`; số lượng `(18, 4)`. Enum lưu mã số ổn định.
- Concurrency token lạc quan: `xmin` (`uint` + `IsRowVersion()`); client gửi lại phiên bản, lệch ⇒ 412.
- Bất biến DB làm được thì làm ở DB: FK, CHECK (`ToTable(t => t.HasCheckConstraint(...))`),
  partial unique index (`HasIndex(...).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL")`).

## Repository, read service và SQL
- Repository phục vụ command: nạp/lưu aggregate, method khóa (`…ForUpdateAsync`). Không quy tắc nghiệp vụ.
- Read service phục vụ query: `AsNoTracking()` + projection thẳng sang DTO của Application; lọc quyền trước phân trang
  và `COUNT`. Không N+1; `Include` có chủ đích.
- Khóa hàng: `ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Bang\" WHERE \"Id\" = {id} FOR UPDATE")` rồi mới nạp entity
  (FOR UPDATE không ghép được với `Include`). Nhiều hàng: `ORDER BY "Id"` để thứ tự khóa thống nhất.
- Khóa logic: `SELECT pg_advisory_xact_lock({key})` — chỉ trong transaction; khóa cố định khai báo thành hằng.
- Hàng đợi/Outbox: `FOR UPDATE SKIP LOCKED` + `UPDATE … RETURNING`.
- SQL thô luôn qua interpolation có tham số (`ExecuteSqlInterpolatedAsync`, `FromSql`); không `ExecuteSqlRaw` với chuỗi ghép.
  Tên cột sắp xếp từ client đi qua allowlist.
- Lỗi `23505`/`23503`/`23514` do tranh chấp nghiệp vụ: dịch sang kết quả có mã lỗi ở tầng gọi, không retry mù.

## Migration
- Tạo (từ `benh_vien_be/`):
  `dotnet ef migrations add <TenMoTa> --project src/QuanLyBenhVien.Persistence --startup-project src/QuanLyBenhVien.API --output-dir Migrations`.
- Đọc lại migration + `AppDbContextModelSnapshot` sinh ra; xem SQL bằng `dotnet ef migrations script`.
- **Không sửa migration đã áp dụng/đã commit**; sửa tiếp bằng migration mới. Không sửa tay snapshot.
- Chuyển migration cũ từ `Infrastructure/Persistence/Migrations/` phải giữ nguyên `MigrationId`; tạo lại migration khởi đầu
  là đổi lịch sử — hỏi người dùng trước.
- Thay đổi phá tương thích (đổi tên/xóa cột, thêm NOT NULL cho bảng có dữ liệu): expand → deploy → backfill → contract.
- Seed danh mục cố định (quyền, vai trò hệ thống) qua `Seed/`, không qua migration `InsertData` trừ khi có lý do.
- `AuditSaveChangesInterceptor` tự ghi `AuditLogs` cho entity `IAuditable` — không ghi audit diff tay.

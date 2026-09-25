---
paths:
  - "**/CleanArchCqrs.Infrastructure/Persistence/**/*.cs"
  - "**/CleanArchCqrs.Infrastructure/Repositories/**/*.cs"
---
# Quy tắc Persistence (PostgreSQL + EF Core code-first)

## Mapping
- Mỗi entity một `IEntityTypeConfiguration<T>` trong `Persistence/Configurations/`, `builder.ToTable("TenSoNhieu")`.
  Tên bảng/cột PascalCase như hiện có — không chuyển snake_case.
- Khóa `Guid` do Domain sinh (`Guid.CreateVersion7()`), cấu hình `ValueGeneratedNever()` khi cần.
- `DateTimeOffset` → `timestamptz`, chỉ lưu offset 0 (Npgsql từ chối offset khác). Ngày sinh `DateOnly` → `date`.
- Tiền `decimal` → `HasPrecision(19, 2)`; tỷ lệ `(7, 4)`; số lượng `(18, 4)`. Enum lưu mã số ổn định.
- Concurrency token lạc quan: `xmin` (`uint` + `IsRowVersion()`); client gửi lại phiên bản, lệch ⇒ 412.
- Bất biến DB làm được thì làm ở DB: FK, CHECK (`ToTable(t => t.HasCheckConstraint(...))`),
  partial unique index (`HasIndex(...).IsUnique().HasFilter("\"EndedAtUtc\" IS NULL")`).

## Repository và SQL
- Repository implement interface ở Domain; chỉ chứa truy cập dữ liệu, không quy tắc nghiệp vụ.
- Query đọc: `AsNoTracking()` + projection. Không N+1; `Include` có chủ đích.
- Khóa hàng: `ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Bang\" WHERE \"Id\" = {id} FOR UPDATE")` rồi mới nạp entity
  (FOR UPDATE không ghép được với `Include`). Nhiều hàng: `ORDER BY "Id"` để thứ tự khóa thống nhất.
- Khóa logic: `SELECT pg_advisory_xact_lock({key})` — chỉ trong transaction; khóa cố định khai báo thành hằng.
- Hàng đợi/Outbox: `FOR UPDATE SKIP LOCKED` + `UPDATE … RETURNING`.
- SQL thô luôn qua interpolation có tham số (`ExecuteSqlInterpolatedAsync`, `FromSql`); không `ExecuteSqlRaw` với chuỗi ghép.
  Tên cột sắp xếp từ client đi qua allowlist.

## Migration
- Tạo: `dotnet ef migrations add <TenMoTa> --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API --output-dir Persistence/Migrations`.
- Đọc lại migration + `AppDbContextModelSnapshot` sinh ra; xem SQL bằng `dotnet ef migrations script`.
- **Không sửa migration đã áp dụng/đã commit**; sửa tiếp bằng migration mới. Không sửa tay snapshot.
- Thay đổi phá tương thích (đổi tên/xóa cột, thêm NOT NULL cho bảng có dữ liệu): expand → deploy → backfill → contract.
- Seed danh mục cố định (quyền, vai trò hệ thống) qua `Persistence/Seed/`, không qua migration `InsertData` trừ khi có lý do.
- `AuditSaveChangesInterceptor` tự ghi `AuditLogs` cho entity `IAuditable` — không ghi audit diff tay.

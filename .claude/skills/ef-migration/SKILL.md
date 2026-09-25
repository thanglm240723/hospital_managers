---
name: ef-migration
description: Tạo và kiểm tra migration EF Core code-first (Npgsql/PostgreSQL) cho HMS. Dùng khi người dùng yêu cầu thêm/đổi bảng, cột, index, ràng buộc.
disable-model-invocation: true
argument-hint: "<mô tả thay đổi schema>"
---
Với `$ARGUMENTS`, chạy từ `dotnet-clean-architecture-cqrs-starter/`:

1. `dotnet tool restore` (tool `dotnet-ef` ghim trong `.config/dotnet-tools.json`).
2. Sửa entity (Domain) và `Infrastructure/Persistence/Configurations/*Configuration.cs`; thêm `DbSet` vào `AppDbContext` nếu là bảng mới.
   Ràng buộc: FK, CHECK, partial unique index, `xmin`, precision tiền, `timestamptz` (rule `persistence.md`).
3. Đặt tên migration mô tả nghiệp vụ, PascalCase (`AddPatientRegistry`, `AddBedAssignmentActiveIndex`), rồi:
   ```
   dotnet ef migrations add <Ten> --project src/CleanArchCqrs.Infrastructure --startup-project src/CleanArchCqrs.API --output-dir Persistence/Migrations
   ```
4. Đọc lại file migration + snapshot: đúng kiểu cột, nullability, tên index/constraint, không có thay đổi ngoài ý muốn
   (drop/recreate cột, đổi kiểu làm mất dữ liệu).
5. Xem SQL: `dotnet ef migrations script <MigrationTruoc> <Ten> --project … --startup-project …`.
6. Kiểm tra an toàn khi bảng đã có dữ liệu; thay đổi phá tương thích ⇒ tách expand/contract thành nhiều migration.
7. Áp dụng DB dev: chạy API với `Database:MigrateOnStartup=true` (Development) hoặc `dotnet ef database update` (sẽ hỏi quyền).
   Không `database drop`, không `--connection` tới DB ngoài dev.
8. Chạy integration test liên quan (`tooling/validate.ps1 -Mode Full`) — Testcontainers áp migration trên DB sạch.
9. Báo cáo: migration đã tạo, SQL chính, rủi ro dữ liệu, bước deploy (production chạy migration là bước riêng).

Không bao giờ sửa migration đã áp dụng/đã commit — sai thì tạo migration mới. `migrations remove` chỉ cho migration chưa áp dụng ở đâu.

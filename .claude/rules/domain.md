---
paths:
  - "**/QuanLyBenhVien.Domain/**/*.cs"
---
# Quy tắc Domain

- Domain chỉ chứa: entity/aggregate, value object, quy tắc nghiệp vụ, domain event, interface repository,
  `IUnitOfWork`/`IUnitOfWorkTransaction`, exception miền (`DomainException`, `NotFoundException`,
  `BusinessRuleViolationException`).
- **Không** `<PackageReference>` hay `<ProjectReference>` nào trong `QuanLyBenhVien.Domain.csproj`. Không `using`
  MediatR, EF Core, ASP.NET, Npgsql, Redis, SDK storage.
- Không attribute persistence (`[Table]`, `[Key]`, `[MaxLength]`, `[NotMapped]`) — cấu hình ở
  `QuanLyBenhVien.Persistence/Configurations/<Module>/` bằng Fluent API.
- Kế thừa `AggregateRoot<Guid>` cho aggregate root, `Entity<Guid>` cho entity con. Không dùng `BaseEntity`/`IAggregateRoot`.
- Private setter + factory method (`User.Register(...)`), constructor private không tham số cho EF. Hợp lệ ngay khi tạo.
- Chuyển trạng thái bằng method nghiệp vụ (`Deactivate()`, `Revoke(reason, now)`), từ chối mọi chuyển trạng thái ngoài
  bảng ánh xạ của đặc tả. Thời điểm truyền vào từ ngoài (`DateTimeOffset now`), không gọi `DateTime.UtcNow` trong entity.
- Domain event: `sealed record XxxDomainEvent(...) : DomainEvent` trong `<Feature>/Events/`; không kế thừa `INotification`.
- Entity cần audit diff implement `IAuditable`.
- Tổ chức theo feature (`Identity/`, `Identity/Sessions/`), không theo loại kỹ thuật (`Entities/`, `Interfaces/`).
- Interface repository phục vụ command (nạp/lưu aggregate, khóa). Truy vấn danh sách/màn hình đi qua read service của
  Application, không thêm method đọc DTO vào repository. Không dùng `PagedResult` (model của Application) trong Domain.
- Hằng quyền: `Identity/Permissions.cs` — module mới thêm class lồng + danh sách `PermissionDefinition`, nối vào `All`.
- Exception miền (`DomainException`, `BusinessRuleViolationException`, `NotFoundException`) chỉ cho vi phạm bất biến mà
  Application lẽ ra đã chặn; lỗi nghiệp vụ dự kiến do Application trả `Result.Failure`.
- Quy tắc nghiệp vụ bất biến: `.claude/rules/hms-business-invariants.md`. Test trực tiếp ở `tests/QuanLyBenhVien.UnitTests/Domain/`.

---
name: feature
description: Lập kế hoạch hoặc triển khai một use case HMS theo vertical slice (Domain → Application → Persistence/Infrastructure → Presentation → Gateway → frontend) giữ đúng Clean Architecture, CQRS, validation, transaction, quyền, audit và test.
argument-hint: "<use case / mã NV>"
---
Với `$ARGUMENTS`:

1. **Nguồn**: tìm mã NV/AT liên quan trong đặc tả nghiệp vụ, mục tương ứng trong đặc tả kỹ thuật v3.1, spec theo chủ đề
   nếu có. Liệt kê mục `OPEN-xx` ảnh hưởng — không tự chốt.
2. **Hiện trạng**: tìm luồng Domain/Application/Persistence/Presentation đang có trước khi thêm file; tái dùng abstraction.
   Backend đang chuyển khung (`ARCHITECTURE.md` §9) — đặt file mới theo khung mới, không vào thư mục mã cũ.
3. **Nghiệm thu**: viết tiêu chí quan sát được (map AT-xx). Phân loại Command hay Query.
4. **Domain**: chỉ thêm hành vi cho bất biến thật (factory, method chuyển trạng thái, domain event). Kiểm tra
   `.claude/rules/hms-business-invariants.md`.
5. **Application**: `Features/<Feature>/<UseCase>/` chứa `XxxCommand`/`XxxQuery` (`ICommand<Result…>`/`IQuery<Result<T>>`),
   handler, validator, DTO response; lỗi dự kiến khai ở `Features/<Feature>/Common/<Feature>Errors.cs` và trả
   `Result.Failure`. Query khai read service port ở `Features/<Feature>/Common/`.
   Command: chọn một cách quản transaction (SaveChanges đơn / `BeginTransactionAsync` + khóa / không ghi);
   xác định idempotency và việc sau commit (skill `transaction-write`). Dữ liệu nhạy cảm ⇒ `IAuditedRequest`.
6. **Persistence**: `Configurations/<Module>/` (Fluent API, FK/CHECK/partial unique index/xmin), `Repositories/<Module>/`,
   `ReadServices/<Module>/`, migration mới (skill `ef-migration`). Adapter ngoài CSDL (Redis, storage…) ở Infrastructure.
7. **Presentation**: module Carter `Endpoints/V1/<Feature>/<Feature>Endpoints.cs`, route `/api/v1/<kebab>`, quyền theo
   `Permissions.cs` (thêm hằng nếu cần), filter CSRF cho request đổi dữ liệu, DTO request `XxxRequest.cs` cùng thư mục,
   map `Result` → HTTP. Đăng ký DI mới (nếu có) ở `API/Composition/`. Cập nhật route Gateway.
8. **Frontend** (nếu thuộc phạm vi): skill `react-feature`.
9. **Test**: unit cho Domain/handler/validator; integration (Testcontainers) cho luồng HTTP, quyền 403, ràng buộc CSDL, tranh chấp đồng thời.
10. **Kiểm tra**: `tooling/validate.ps1` mức phù hợp (`VALIDATION.md`), đọc lại diff; `/review-diff` khi thay đổi đáng kể.

Mặc định người dùng tự viết code: nếu chưa được yêu cầu implement, trả về **kế hoạch** theo các bước trên (file cần tạo/sửa,
signature, tiêu chí "Xong khi"), không sửa source. Giữ thay đổi tối thiểu, không tạo abstraction cho nhu cầu giả định.

---
name: feature
description: Lập kế hoạch hoặc triển khai một use case HMS theo vertical slice (Domain → Application → Infrastructure → API → Gateway → frontend) giữ đúng Clean Architecture, CQRS, validation, transaction, quyền, audit và test.
argument-hint: "<use case / mã NV>"
---
Với `$ARGUMENTS`:

1. **Nguồn**: tìm mã NV/AT liên quan trong đặc tả nghiệp vụ, mục tương ứng trong đặc tả kỹ thuật v3.1, spec theo chủ đề
   nếu có. Liệt kê mục `OPEN-xx` ảnh hưởng — không tự chốt.
2. **Hiện trạng**: tìm luồng Domain/Application/API/Persistence đang có trước khi thêm file; tái dùng abstraction.
3. **Nghiệm thu**: viết tiêu chí quan sát được (map AT-xx). Phân loại Command hay Query.
4. **Domain**: chỉ thêm hành vi cho bất biến thật (factory, method chuyển trạng thái, domain event). Kiểm tra
   `.claude/rules/hms-business-invariants.md`.
5. **Application**: `<Feature>/Commands|Queries/<UseCase>/` + handler file riêng + validator + DTO.
   Command: chọn một cách quản transaction (SaveChanges đơn / `BeginTransactionAsync` + khóa / không ghi);
   xác định idempotency và việc sau commit (skill `transaction-write`). Dữ liệu nhạy cảm ⇒ `IAuditedRequest`.
6. **Persistence**: configuration Fluent API, ràng buộc CSDL (FK/CHECK/partial unique index/xmin), repository, migration
   mới (skill `ef-migration`).
7. **API**: controller mỏng `api/v1/<kebab>`, `[HasPermission]` (thêm hằng ở `Permissions.cs` nếu cần), `[CsrfProtected]`,
   DTO request ở `Contracts/`. Cập nhật route Gateway.
8. **Frontend** (nếu thuộc phạm vi): skill `react-feature`.
9. **Test**: unit cho Domain/validator; integration (Testcontainers) cho luồng HTTP, quyền 403, ràng buộc CSDL, tranh chấp đồng thời.
10. **Kiểm tra**: `tooling/validate.ps1` mức phù hợp (`VALIDATION.md`), đọc lại diff; `/review-diff` khi thay đổi đáng kể.

Mặc định người dùng tự viết code: nếu chưa được yêu cầu implement, trả về **kế hoạch** theo các bước trên (file cần tạo/sửa,
signature, tiêu chí "Xong khi"), không sửa source. Giữ thay đổi tối thiểu, không tạo abstraction cho nhu cầu giả định.

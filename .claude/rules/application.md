---
paths:
  - "**/CleanArchCqrs.Application/**/*.cs"
---
# Quy tắc Application

## Cấu trúc
- `<Feature>/Commands/<UseCase>/{XxxCommand.cs, XxxCommandHandler.cs}`, `<Feature>/Queries/<UseCase>/…`,
  `<Feature>/Validators/XxxValidator.cs`, `<Feature>/Models/XxxDto.cs`. Hằng/helper riêng feature đặt ở gốc feature
  (`Auth/AuthMessages.cs`, `Users/AdminSafety.cs`).
- Thành phần dùng chung: `Common/{Behaviors, Exceptions, Interfaces, Models, Security, Auditing}`.
- Command/Query là `record` implement `IRequest<T>`/`IRequest`. Tên theo động từ nghiệp vụ.
- Phụ thuộc chỉ Domain + MediatR + FluentValidation + `Microsoft.Extensions.Logging.Abstractions`.
  Không DbContext, không `Microsoft.AspNetCore.*`, không StackExchange.Redis. Cần gì thì khai port trong
  `Common/Interfaces/` và implement ở Infrastructure/API.

## Transaction
- Không có pipeline behavior transaction. Mỗi command tự chọn một trong:
  1. một lần `IUnitOfWork.SaveChangesAsync` (EF tự bọc transaction);
  2. `await using var tx = await _unitOfWork.BeginTransactionAsync(ct)` → khóa (`…ForUpdateAsync`, advisory lock) →
     nạp lại dữ liệu **sau** khi khóa → kiểm tra bất biến → ghi → `SaveChangesAsync` → `tx.CommitAsync`.
     Dispose chưa commit ⇒ rollback;
  3. không ghi DB.
- Kết quả phải tồn tại dù request lỗi (ghi nhận reuse, thu hồi family, audit thất bại đăng nhập) ⇒ commit trước rồi mới ném lỗi.
- Không gọi Redis, HTTP, storage, SMS/email khi đang giữ transaction. Xóa cache: `ICacheInvalidator.Invalidate…`
  (ghi hàng `CacheInvalidations` trong transaction) → commit → `FlushAsync`. Module nghiệp vụ gửi thông báo/tích hợp
  qua Outbox (đặc tả §11.3).
- Command có `Idempotency-Key` (§11.1): nhận key bằng INSERT + unique constraint trong cùng transaction với business write.

## Validation và lỗi
- Validator FluentValidation cho mọi input từ client; `ValidationBehavior` gom lỗi thành `ValidationException` (400).
- Lỗi dự kiến: `ConflictException(ErrorCodes.X, "message tiếng Việt")` (409), `ForbiddenException` (403),
  `UnauthorizedException` (401), `TooManyRequestsException` (429), `NotFoundException` (404, Domain),
  `BusinessRuleViolationException` (409, Domain). Mã mới thêm vào `Common/Exceptions/ErrorCodes.cs` (snake_case).
- Message cho người dùng tiếng Việt, không chứa dữ liệu nhạy cảm hay chi tiết nội bộ.

## Audit, người dùng, thời gian
- Request trả dữ liệu nhạy cảm implement `IAuditedRequest` (`AuditAction`, `AuditResourceType`, `AuditResourceId`) —
  khai báo tường minh. Sự kiện bảo mật khác ghi qua `IAuditRecorder.Record(AuditActions.X, AuditResult.Y, …)`;
  hằng hành động ở `Common/Auditing/AuditActions.cs`.
- Người dùng hiện tại: `ICurrentUser` (`UserId`, …); thông tin request (IP, User-Agent): `IRequestContext`.
- Thời gian: inject `TimeProvider`, gọi `GetUtcNow()`.
- Luôn nhận và truyền `CancellationToken ct`.

## Query
- Lọc phạm vi quyền trong truy vấn, **trước** phân trang và đếm; không tải hết rồi lọc trong bộ nhớ.
- Trả DTO/projection, không trả entity. Phân trang dùng `PagedResult<T>(Items, PageNumber, PageSize, TotalCount)`
  (`Common/Models/PagedResult.cs`),
  `pageSize` kẹp trong 1..100, `ORDER BY` có `Id` làm khóa phân xử.

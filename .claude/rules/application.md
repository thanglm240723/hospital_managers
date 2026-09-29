---
paths:
  - "**/QuanLyBenhVien.Application/**/*.cs"
---
# Quy tắc Application

## Cấu trúc
- Vertical slice: `Features/<Feature>/<UseCase>/` chứa mọi file của một use case:
  `XxxCommand.cs` | `XxxQuery.cs`, `XxxCommandHandler.cs` | `XxxQueryHandler.cs`, `XxxCommandValidator.cs`, DTO response
  riêng (`MeDto.cs`). Ví dụ: `Features/Auth/Login/`, `Features/Auth/GetMe/`.
- Dùng chung trong một feature: `Features/<Feature>/Common/` — `<Feature>Errors.cs`, port của feature
  (`IAuthReadService`, `IAccessTokenIssuer`, `ILoginAttemptLimiter`…), DTO/record dùng bởi nhiều use case (`AuthTokensResult`).
- Dùng chung toàn app: `Common/Messaging` (`ICommand<T>`, `IQuery<T>`), `Common/Results` (`Result`, `Result<T>`, `Error`),
  `Common/Models` (`PagedResult<T>`), `Common/Identity` (`ICurrentUser`, `IRequestContext`), `Common/Caching`,
  `Common/Auditing` (`IAuditWriter`), `Common/Security` (`IPasswordHasher`). Behavior ở `Behaviors/`.
  Đăng ký DI ở `DependencyInjection.cs`.
- Không tạo thư mục theo loại kỹ thuật (`Commands/`, `Queries/`, `Handlers/`, `Validators/`, `Models/`).
- Namespace khớp đường dẫn: `QuanLyBenhVien.Application.Features.<Feature>.<UseCase>`.
- Phụ thuộc chỉ Domain + MediatR + FluentValidation + `Microsoft.Extensions.Logging.Abstractions`.
  Không DbContext, không `Microsoft.AspNetCore.*`, không StackExchange.Redis, không SDK ngoài. Cần gì thì khai port và
  implement ở Persistence (dữ liệu), Infrastructure (Redis/bảo mật/ngoài) hoặc API (thông tin request).

## Command, Query, Handler
- Command: `public sealed record XxxCommand(...) : ICommand<Result>` hoặc `ICommand<Result<T>>`.
  Query: `public sealed record XxxQuery(...) : IQuery<Result<T>>`. Tên theo động từ nghiệp vụ.
- Handler `internal sealed class`, implement `IRequestHandler<TRequest, TResponse>`, inject qua primary constructor.
- Command ghi qua aggregate + repository (interface ở Domain) + `IUnitOfWork`. Query đọc qua read service (port ở
  `Features/<Feature>/Common/`, implement ở `Persistence/ReadServices/<Module>/`), trả DTO — không nạp aggregate để đọc.
- Không trả entity Domain ra ngoài Application.

## Kết quả và lỗi
- Lỗi nghiệp vụ dự kiến (sai thông tin đăng nhập, trùng, không đủ quyền theo tài nguyên, phiên hết hạn, tranh chấp…)
  ⇒ `return Result.Failure(<Feature>Errors.X)`. Không ném exception cho luồng dự kiến.
- `<Feature>Errors` là `static class` khai các `Error` cố định: mã snake_case ổn định (hợp đồng với frontend), message
  tiếng Việt không chứa dữ liệu nhạy cảm/chi tiết nội bộ, loại lỗi (validation/unauthorized/forbidden/not found/conflict/
  precondition/too many requests) để Presentation chọn status. Không đổi mã đã phát hành.
- Validator FluentValidation (`internal sealed class XxxCommandValidator : AbstractValidator<XxxCommand>`) cho mọi input từ
  client; `ValidationBehavior` gom lỗi và ném `ValidationException` (400).
- Exception chỉ còn cho vi phạm bất biến Domain và lỗi bất ngờ; exception handler ở API map về cùng định dạng Problem Details.

## Transaction
- Không có pipeline behavior transaction. Mỗi command tự chọn một trong:
  1. một lần `IUnitOfWork.SaveChangesAsync` (EF tự bọc transaction);
  2. `await using var tx = await unitOfWork.BeginTransactionAsync(ct)` → khóa (`…ForUpdateAsync`, advisory lock) →
     nạp lại dữ liệu **sau** khi khóa → kiểm tra bất biến → ghi → `SaveChangesAsync` → `tx.CommitAsync`.
     Dispose chưa commit ⇒ rollback;
  3. không ghi DB.
- Kết quả phải tồn tại dù request thất bại (ghi nhận reuse, thu hồi family, audit đăng nhập sai) ⇒ commit trước rồi mới
  `return Result.Failure(...)`.
- Không gọi Redis, HTTP, storage, SMS/email khi đang giữ transaction. Xóa cache: `ICacheInvalidator.Invalidate…`
  (ghi hàng `CacheInvalidations` trong transaction) → commit → `FlushAsync`. Module nghiệp vụ gửi thông báo/tích hợp
  qua Outbox (đặc tả §11.3).
- Command có `Idempotency-Key` (§11.1): nhận key bằng INSERT + unique constraint trong cùng transaction với business write.

## Audit, người dùng, thời gian
- Pipeline đích: `LoggingBehavior` → `ValidationBehavior` → `AuditBehavior`. Request trả dữ liệu nhạy cảm implement
  `IAuditedRequest` tường minh; `AuditBehavior` ghi audit bền vững qua `IAuditWriter` **trước** khi trả, ghi lỗi thì không trả
  dữ liệu (§3.3). `AuditBehavior`/`IAuditedRequest` chưa có trong khung mới — thêm khi làm use case nhạy cảm đầu tiên.
- Người dùng hiện tại: `ICurrentUser`; thông tin request (IP, User-Agent): `IRequestContext`.
- Thời gian: inject `TimeProvider`, gọi `GetUtcNow()`.
- Luôn nhận và truyền `CancellationToken`.

## Query và phân trang
- Lọc phạm vi quyền trong truy vấn, **trước** phân trang và đếm; không tải hết rồi lọc trong bộ nhớ.
- Phân trang dùng `PagedResult<T>` (`Common/Models/PagedResult.cs`) với `Items`, `PageNumber`, `PageSize`, `TotalCount`;
  `pageSize` kẹp trong 1..100, `ORDER BY` có `Id` làm khóa phân xử.

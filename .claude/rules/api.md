---
paths:
  - "**/CleanArchCqrs.API/**/*.cs"
---
# Quy tắc API

- Controller mỏng: bind → map sang command/query → `ISender.Send(…, ct)` → trả kết quả. Không truy vấn EF, không gọi
  Redis/Infrastructure, không quy tắc nghiệp vụ trong controller.
- Route: `[Route("api/v1/<tài-nguyên-kebab>")]` viết tay (không `[controller]` — sinh sai với tên nhiều từ).
  Command nghiệp vụ: `POST {id}/<động-từ>`. Không đổi dữ liệu bằng `GET`. Route nội bộ cho Gateway: `internal/...`
  + `[InternalApiKey]`.
- Deny-by-default: fallback policy yêu cầu đăng nhập. Endpoint công khai phải ghi rõ `[AllowAnonymous]` và có lý do.
- Quyền: `[HasPermission(Permissions.X.Y)]` (hằng trong Domain). Endpoint đổi dữ liệu: `[CsrfProtected]`.
  Endpoint được gọi khi còn bắt đổi mật khẩu: `[AllowWhilePasswordChangeRequired]`.
- DTO request: `Contracts/<Feature>/XxxRequest.cs` (record). DTO response lấy từ `Application/<Feature>/Models/`.
  Không trả entity Domain. JSON camelCase, bỏ trường null.
- Lỗi: ném exception, `Errors/GlobalExceptionHandler` map sang Problem Details với `code`, `traceId`, `errors`.
  Không tự `return BadRequest("…")` với chuỗi tự do. 403 từ policy đi qua `ProblemAuthorizationResultHandler`.
- Cookie auth (`__Host-rt`, CSRF) chỉ ghi qua `Auth/AuthCookieWriter`.
- `ICurrentUser`/`IRequestContext` implement ở `Services/` từ claim đã validate — không tin header do client tự gửi.
- `Program.cs` là composition root: middleware theo thứ tự hiện có (ForwardedHeaders → CorrelationId → ExceptionHandler
  → Serilog request logging → Authentication → UserLogContext → Authorization).
- Thêm/sửa route ⇒ cập nhật `CleanArchCqrs.Gateway/appsettings.json` và frontend gọi tới (xem skill `api-contract-change`).
- Swagger chỉ bật ở Development.

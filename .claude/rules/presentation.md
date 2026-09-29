---
paths:
  - "**/QuanLyBenhVien.Presentation/**/*.cs"
---
# Quy tắc Presentation (endpoint Minimal API + Carter)

`QuanLyBenhVien.Presentation` chỉ tham chiếu Application (+ `Microsoft.AspNetCore.App`, Carter). Không truy vấn EF,
không gọi Persistence/Infrastructure/Redis, không quy tắc nghiệp vụ.

## Bố cục
- `Endpoints/V1/<Feature>/<Feature>Endpoints.cs` — `public sealed class … : ICarterModule`, một module cho một tài nguyên.
- `Endpoints/V1/<Feature>/XxxRequest.cs` — DTO request (`public sealed record`), chỉ cho body/query cần bind.
- `Endpoints/V2/` chỉ tạo khi hợp đồng HTTP thật sự cần phiên bản mới; V2 có thể gọi cùng use case với V1.
- Thành phần HTTP dùng chung cho endpoint (map `Result` → `IResult`, extension/filter quyền và CSRF, ghi cookie auth)
  đặt trong Presentation, ngoài thư mục `Endpoints/`.

## Endpoint
- Nhóm route: `app.MapGroup("/api/v1/<tài-nguyên-kebab>").WithTags("…")`. Command nghiệp vụ: `POST {id}/<động-từ>`.
  Không đổi dữ liệu bằng `GET`. Mỗi route `.WithName("<Tên>V1")` duy nhất.
- Handler là `private static async Task<IResult>` nhận request DTO, `ISender`, `CancellationToken` (và `HttpContext` khi
  cần cookie/header): bind → tạo Command/Query → `sender.Send(…, ct)` → map kết quả. Không logic khác.
- Deny-by-default: fallback policy ở API yêu cầu đăng nhập. Route công khai ghi rõ `.AllowAnonymous()` và có lý do
  (hiện chỉ `login`, `refresh`, `logout`).
- Quyền hành động khai trên route theo hằng `Permissions.X.Y` (Domain); endpoint đổi dữ liệu dựa trên cookie có filter CSRF;
  endpoint được gọi khi còn bắt đổi mật khẩu phải được đánh dấu cho phép tường minh. Route nội bộ cho Gateway:
  `/internal/...` + kiểm tra `X-Internal-Key`.
- `PatientId`, `OrganizationId`, quyền, cờ `Paid`/`Emergency` không lấy từ request — handler tự suy ra.

## Kết quả và lỗi
- `Result` thành công ⇒ `200`/`201`/`204` phù hợp; thất bại ⇒ Problem Details (RFC 9457) có `code`, `traceId`, `errors`,
  status theo loại `Error`. Một chỗ map duy nhất, cùng định dạng với exception handler ở API.
- Không tự `Results.BadRequest("…")` với chuỗi tự do. Không lộ stack trace, SQL, token.
- Response là DTO của Application; JSON camelCase, bỏ trường null. Không trả entity.
- Cookie `__Host-rt` và cookie CSRF chỉ ghi qua một thành phần ghi cookie duy nhất.

## Đồng bộ
- Thêm/sửa route ⇒ cập nhật `QuanLyBenhVien.Gateway/appsettings.json` và frontend gọi tới (skill `api-contract-change`).
- Test HTTP: integration test ở `tests/QuanLyBenhVien.IntegrationTests/<Feature>/` qua `ApiFactory`.

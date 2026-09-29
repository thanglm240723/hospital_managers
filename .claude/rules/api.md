---
paths:
  - "**/QuanLyBenhVien.API/**/*"
---
# Quy tắc API (host / composition root)

`QuanLyBenhVien.API` là tiến trình chạy: tham chiếu Application, Persistence, Infrastructure, Presentation và ghép chúng
lại. **Không** chứa endpoint (ở Presentation), không logic nghiệp vụ, không truy vấn EF.

## Bố cục
- `Program.cs` — cấu hình host, Serilog, pipeline middleware, `MapCarter()`, health check.
- `Composition/` — chỉ đăng ký DI (gọi extension của từng project), không logic.
- `Middleware/` — middleware HTTP cắt ngang: correlation id, log context người dùng, exception handler.
- `Health/` — đăng ký liveness/readiness và health check phụ thuộc.
- `Security/` — JWT bearer, fallback policy (deny-by-default), policy quyền, implement `ICurrentUser`/`IRequestContext`
  từ claim đã validate — không tin header do client tự gửi.

Trạng thái: `Program.cs` hiện còn bản cũ (`AddControllers`, `MapControllers`, namespace cũ) — xem `ARCHITECTURE.md` §9.

## Pipeline
- Thứ tự middleware: ForwardedHeaders → CorrelationId → ExceptionHandler → Serilog request logging → Authentication →
  UserLogContext → Authorization → endpoint.
- Exception handler map `ValidationException`, exception miền và lỗi bất ngờ sang Problem Details (`code`, `traceId`,
  `errors`), cùng định dạng với map `Result` ở Presentation. Lỗi 403 từ policy cũng trả Problem Details.
- `ForwardedHeaders:KnownProxies` chỉ chứa proxy tin cậy (Gateway).
- Swagger chỉ bật ở Development.

## Cấu hình
- Options bind + validate khi khởi động; secret (`Jwt:SigningKey`, `Auth:CsrfKey`, `Auth:InternalApiKey`,
  `Seed:AdminPassword`) chỉ từ user-secrets/secret store — `appsettings*.json` để rỗng.
- `Database:MigrateOnStartup` chỉ `true` ở Development.
- Chỉ thêm tham chiếu tới capability/adapter mà tiến trình này thật sự dùng; worker nặng nên là host riêng.

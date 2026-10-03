# Security

Chỉ chứa các adapter và cấu hình bảo mật gắn trực tiếp với runtime ASP.NET Core và `HttpContext`:
- **Context port**: Hiện thực interface từ Application cần đọc request (VD: `CurrentUser : ICurrentUser`, `HttpRequestContext : IRequestContext`).
- **Authorization & Auth setup**: Provider/handler phân quyền động (`PermissionPolicyProvider`, `PermissionAuthorizationHandler`), map lỗi 401/403 kèm audit denial (`ProblemAuthorizationResultHandler`), cấu hình JWT bearer (`JwtAuthenticationSetup`).
- **Middleware & Log policy**: Middleware bảo mật host (`PasswordChangeGateMiddleware`), che `***` dữ liệu nhạy cảm khi log (`SensitiveDataDestructuringPolicy`).

Không để ở đây: endpoint (thuộc Presentation), use case (Application), mã hóa/hash token (Infrastructure), DbContext (Persistence).

*Ví dụ thêm class*: `public sealed class DeviceContext(IHttpContextAccessor acc) : IDeviceContext { ... }`

# Middleware

Chỉ middleware HTTP cắt ngang: correlation id, log context người dùng (`UserId`, `SessionFamilyId`), exception handler
(map `ValidationException`, exception miền và lỗi bất ngờ sang Problem Details cùng định dạng với Presentation).

Thứ tự trong `Program.cs`: ForwardedHeaders → CorrelationId → ExceptionHandler → Serilog request logging →
Authentication → UserLogContext → Authorization → endpoint.

HMS không có middleware xác định tenant; phạm vi tổ chức/cơ sở kiểm tra trong handler theo quyền và phân công.

# Health

Đăng ký liveness/readiness và health check phụ thuộc. `GET /health`: PostgreSQL lỗi ⇒ `Unhealthy`;
Redis lỗi ⇒ `Degraded` (hệ thống vẫn chạy bằng DB). Endpoint health cho phép ẩn danh, không trả chi tiết nội bộ.

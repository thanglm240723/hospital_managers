---
paths:
  - "**/QuanLyBenhVien.Gateway/**/*"
---
# Quy tắc Gateway (YARP)

- Gateway **không** tham chiếu project nào trong solution; chỉ chia sẻ hợp đồng qua cấu hình (khóa JWT, internal key,
  định dạng payload phiên trong Redis — `Auth/SessionCachePayload.cs` phải khớp bản ở Infrastructure `Caching/`).
- Trách nhiệm: định tuyến, validate JWT HS256 + phiên còn sống (`session:{fid}` Redis → miss/lỗi → `POST /internal/sessions/validate`),
  rate limit theo IP cho login/refresh, correlation id, xóa header `X-Internal-*` từ client. **Phân quyền do API quyết định.**
- Route công khai chỉ: `POST /api/v1/auth/login|refresh|logout`. Mọi route khác bắt buộc token hợp lệ.
- Thêm route trong `appsettings.json` → `ReverseProxy:Routes`; không cắt prefix path. Route nghiệp vụ mới dùng `/api/v1/...`.
  Route cũ `/api/appointments/**` không dùng (đặc tả không có đặt lịch).
- `ForwardedHeaders:KnownProxies` chỉ chứa proxy tin cậy; không tin `X-Forwarded-For` từ nơi khác (IP rate limit dựa vào nó).
- Redis lỗi: không chặn toàn hệ thống — rơi về hỏi API, health `Degraded`.
- Lỗi trả Problem Details qua `Errors/ProblemResponseWriter.cs`, cùng định dạng với API.
- Test: `tests/QuanLyBenhVien.IntegrationTests/Gateway/` (GatewayFactory + RecordingHandler thay downstream).

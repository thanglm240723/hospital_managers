# CleanArchCqrs.Gateway

API Gateway (YARP reverse proxy) — điểm vào công khai duy nhất đứng trước các backend service.

Tầng này **xác thực** rồi mới định tuyến: validate JWT HS256 (cùng khoá với API) và kiểm tra phiên
`session:{fid}` trong Redis — không có key hoặc Redis lỗi thì hỏi `POST /internal/sessions/validate` của API.
Route public (không cần token): `POST /api/v1/auth/login|refresh|logout`. Mọi route khác phải có token hợp lệ.
**Phân quyền** vẫn do API quyết định. Header `X-Internal-*` do client gửi bị xoá trước khi forward.
Chạy API bằng profile `http` (Gateway gọi `http://localhost:5289`).

## Chạy

```bash
# Terminal 1 - backend
dotnet run --project src/CleanArchCqrs.API

# Terminal 2 - gateway
dotnet run --project src/CleanArchCqrs.Gateway
```

| Service | HTTP | HTTPS |
|---|---|---|
| Gateway | http://localhost:5100 | https://localhost:7100 |
| CleanArchCqrs.API | http://localhost:5289 | https://localhost:7163 |

Client chỉ gọi vào gateway (`:5100`). Sau khi có gateway, các API service nên bỏ expose ra ngoài.

## Bảng định tuyến

| Route | Path | Cluster |
|---|---|---|
| `auth-login-route`, `auth-refresh-route`, `auth-logout-route` | `POST /api/v1/auth/login\|refresh\|logout` (public) | `identity-cluster` |
| `auth-route` | `/api/v1/auth/{**catch-all}` | `identity-cluster` |
| `users-route` | `/api/v1/users/{**catch-all}` | `identity-cluster` |
| `roles-route` | `/api/v1/roles/{**catch-all}` | `identity-cluster` |
| `permissions-route` | `/api/v1/permissions/{**catch-all}` | `identity-cluster` |
| `patients-route` | `/api/patients/{**catch-all}` | `patients-cluster` |
| `doctors-route` | `/api/doctors/{**catch-all}` | `doctors-cluster` |
| `appointments-route` | `/api/appointments/{**catch-all}` | `appointments-cluster` |
| `medical-records-route` | `/api/medical-records/{**catch-all}` | `medical-records-cluster` |
| `billing-route` | `/api/billing/{**catch-all}` | `billing-cluster` |

Mọi cluster hiện đều trỏ về `http://localhost:5289/` (monolith `CleanArchCqrs.API`). Khi tách một service ra
chạy riêng, chỉ cần đổi `Address` của cluster tương ứng — route và client không đổi.

Path **không bị cắt prefix**: `/api/patients/1` ở gateway đi tới `/api/patients/1` ở downstream, nên controller
downstream giữ nguyên `[Route("api/[controller]")]`.

Path không khớp route nào → gateway trả 404, request không đi tới đâu cả.

## Thêm một service mới

1. Thêm cluster vào `ReverseProxy:Clusters` với `Address` của service.
2. Thêm route vào `ReverseProxy:Routes`, trỏ `ClusterId` vào cluster đó.

Không cần sửa code C#.

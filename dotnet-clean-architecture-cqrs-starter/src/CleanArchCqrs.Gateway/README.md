# CleanArchCqrs.Gateway

API Gateway (YARP reverse proxy) — điểm vào công khai duy nhất đứng trước các backend service.

Tầng này **chỉ định tuyến**. Không business logic, không xác thực. YARP đọc `Routes` / `Clusters`
từ `appsettings.json`, không hardcode trong code C#.

Header của request được forward nguyên vẹn xuống destination (mặc định của YARP), bao gồm
`Authorization` — nên **backend tự phát hành và tự validate token**.

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
| `auth-route` | `/api/auth/{**catch-all}` | `identity-cluster` |
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

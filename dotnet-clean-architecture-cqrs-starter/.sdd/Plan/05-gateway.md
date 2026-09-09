# 05 — Gateway

**Phần này đã code xong.** File này ghi lại cách nó hoạt động và những gì cần làm về sau.

## Hiện trạng

`src/CleanArchCqrs.Gateway/` — 3 file code, tất cả rất ngắn:

| File | Nội dung |
|---|---|
| `Program.cs` | `AddGatewayReverseProxy()` → `MapReverseProxy()`. Hết. |
| `DependencyInjection/GatewayServiceExtensions.cs` | `AddReverseProxy().LoadFromConfig(...)` |
| `appsettings.json` | 6 route + 6 cluster |

Không auth, không CORS, không rate limit — đúng như bạn chốt. Header (kể cả `Authorization`)
được YARP forward nguyên vẹn xuống destination, nên **API tự validate token**.

## 6 route hiện có

Tất cả cluster đang trỏ `http://localhost:5289/` (monolith).

```
/api/auth/**              → identity-cluster
/api/patients/**          → patients-cluster
/api/doctors/**           → doctors-cluster
/api/appointments/**      → appointments-cluster
/api/medical-records/**   → medical-records-cluster
/api/billing/**           → billing-cluster
```

Path **không bị cắt prefix**. `/api/patients/1` ở gateway → `/api/patients/1` ở API.
Path không khớp route nào → gateway trả 404, không đi đâu cả.

## Kiểm tra nhanh gateway còn sống

```bash
# API tắt, gateway bật:
curl -o /dev/null -w "%{http_code}\n" http://localhost:5100/api/patients      # 502 = có proxy, backend chết
curl -o /dev/null -w "%{http_code}\n" http://localhost:5100/api/khong-co-that # 404 = gateway tự trả
```

Hai mã khác nhau chứng minh route đang được nạp đúng.

## Việc cần làm về sau

### Khi thêm module mới ngoài 6 module đã có

Sửa `appsettings.json`, **không đụng code C#**:

```json
"Routes": {
  "pharmacy-route": {
    "ClusterId": "pharmacy-cluster",
    "Match": { "Path": "/api/pharmacy/{**catch-all}" }
  }
},
"Clusters": {
  "pharmacy-cluster": {
    "Destinations": { "primary": { "Address": "http://localhost:5289/" } }
  }
}
```

### Khi tách 1 module ra service riêng

Chỉ đổi `Address` của cluster tương ứng. Route giữ nguyên, FE không phải sửa gì.

### Khi FE gọi trực tiếp từ browser

⚠ Sẽ dính **CORS** ngay lần gọi đầu (FE `:9000`, gateway `:5100` — khác origin).
Hai hướng, chọn 1:

1. **Thêm CORS vào gateway** — `AddCors` + `UseCors`, allow origin `http://localhost:9000`,
   `AllowCredentials` nếu dùng cookie. Đúng vai trò gateway.
2. **Proxy từ webpack dev server** — thêm `proxy` vào `config/webpackDevServer.config.js`
   trỏ `/api` sang `:5100`. Chỉ giải quyết được lúc dev, production vẫn cần cách 1.

Plan FE ([06-fe-nen-tang.md](06-fe-nen-tang.md) bước 6.4) đi theo **cách 2 cho dev**, và ghi
cách 1 là việc phải làm trước khi deploy.

### Trước khi lên production

- [ ] CORS (xem trên)
- [ ] Rate limiting — `AddRateLimiter` theo IP, chặn brute-force `/api/auth/login`
- [ ] Health check — `/health` cho gateway, `ActiveHealthCheck` tới từng cluster
- [ ] Correlation ID — sinh `X-Correlation-ID`, forward xuống, log ở cả hai tầng
- [ ] Bỏ expose `:5289` ra ngoài — chỉ gateway được public
- [ ] HTTPS bắt buộc, chỉ mở `:7100`

Tất cả đều thêm vào `GatewayServiceExtensions.cs` + `Program.cs`, không phá cấu trúc hiện tại.

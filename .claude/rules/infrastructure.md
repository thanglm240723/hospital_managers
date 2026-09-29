---
paths:
  - "**/QuanLyBenhVien.Infrastructure/**/*.cs"
---
# Quy tắc Infrastructure

`QuanLyBenhVien.Infrastructure` chứa adapter **không phải CSDL**: Redis, bảo mật (JWT, hash mật khẩu, refresh token, CSRF),
worker nền, health check, sau này object storage/SMS/email. Tham chiếu Domain + Application; **không** tham chiếu
Persistence, không dùng DbContext/EF. Cần đọc/ghi DB (ví dụ worker xử lý `CacheInvalidations`) thì khai port ở Application
và để Persistence implement.

Trạng thái: thư mục `Persistence/`, `Repositories/`, `Identity/` và `DependencyInjection/` hiện là mã cũ chờ chuyển
(`ARCHITECTURE.md` §9) — không thêm mã mới vào đó.

- Implement port của Application (`Common/**`, `Features/<Feature>/Common/`). Không đặt quy tắc nghiệp vụ trong adapter;
  dịch lỗi của nhà cung cấp tại biên (ví dụ `RedisFailure.Is(ex)`).
- Đăng ký DI qua một extension công khai của project; options bind từ cấu hình (`JwtOptions`, `AuthOptions`) và
  validate khi khởi động (`ValidateOnStart`) — secret rỗng/ngắn thì fail sớm.
- Client nặng dùng lại (singleton `IConnectionMultiplexer`), không tạo mới mỗi request.

## Redis
- Redis là cache/bộ đếm, **không** phải nguồn sự thật. Mọi đường đọc phải rơi về DB khi Redis lỗi; health `Degraded`.
- Key tập trung ở `Caching/CacheKeys.cs` (`session:{familyId}`, `perm:{userId}`, …).
- Ghi cache sau khi đọc DB phải qua `GuardedCacheWrite` (chặn ghi đè giá trị cũ sau khi đã invalidate).
- Bộ đếm có hạn (rate limit): INCR + EXPIRE nguyên tử (Lua/transaction), tránh key không bao giờ hết hạn.
- Xóa cache sau thay đổi dữ liệu: bảng `CacheInvalidations` + `CacheInvalidationWorker` — không gọi Redis trong transaction DB.
- Payload phiên trong Redis (`SessionCachePayload`) phải khớp bản ở Gateway.

## Bảo mật
- Mật khẩu: `PasswordHasher` (ASP.NET Identity hasher) — không tự viết thuật toán.
- JWT HS256: validate algorithm, issuer, audience, lifetime, chữ ký; khóa từ cấu hình/secret store.
- Refresh token 256 bit ngẫu nhiên (`RandomNumberGenerator`), DB chỉ lưu SHA-256. CSRF token gắn phiên, so sánh
  thời gian hằng (`CryptographicOperations.FixedTimeEquals`).
- Không log secret, token, hash token, mật khẩu.

## Thời gian
- Dùng `TimeProvider` (đăng ký `TimeProvider.System`), không `DateTime.UtcNow` trực tiếp — test dùng `FakeTimeProvider`.

---
paths:
  - "**/CleanArchCqrs.Infrastructure/Caching/**/*.cs"
  - "**/CleanArchCqrs.Infrastructure/Identity/**/*.cs"
  - "**/CleanArchCqrs.Infrastructure/Security/**/*.cs"
  - "**/CleanArchCqrs.Infrastructure/Auditing/**/*.cs"
  - "**/CleanArchCqrs.Infrastructure/HealthChecks/**/*.cs"
  - "**/CleanArchCqrs.Infrastructure/DependencyInjection/**/*.cs"
---
# Quy tắc Infrastructure (ngoài Persistence)

- Infrastructure implement port của Application (`Common/Interfaces/`). Không đặt quy tắc nghiệp vụ trong adapter;
  dịch lỗi của nhà cung cấp tại biên (ví dụ `RedisFailure.Is(ex)`).
- Đăng ký DI trong `DependencyInjection/InfrastructureServiceExtensions.cs`; options bind từ cấu hình
  (`JwtOptions`, `AuthOptions`, `SeedOptions`) và validate khi khởi động — secret rỗng thì fail sớm.
- Client nặng dùng lại (singleton `IConnectionMultiplexer`), không tạo mới mỗi request.

## Redis
- Redis là cache/bộ đếm, **không** phải nguồn sự thật. Mọi đường đọc phải rơi về DB khi Redis lỗi; health `Degraded`.
- Key tập trung ở `Caching/CacheKeys.cs` (`session:{familyId}`, `perm:{userId}`, …).
- Ghi cache sau khi đọc DB phải qua `GuardedCacheWrite` (chặn ghi đè giá trị cũ sau khi đã invalidate).
- Bộ đếm có hạn (rate limit): INCR + EXPIRE nguyên tử (Lua/transaction), tránh key không bao giờ hết hạn.
- Xóa cache sau thay đổi dữ liệu: bảng `CacheInvalidations` + `CacheInvalidationWorker` — không gọi Redis trong transaction DB.

## Bảo mật
- Mật khẩu: `PasswordHasher` (ASP.NET Identity hasher) — không tự viết thuật toán.
- JWT HS256: validate algorithm, issuer, audience, lifetime, chữ ký; khóa từ cấu hình/secret store.
- Refresh token 256 bit ngẫu nhiên (`RandomNumberGenerator`), DB chỉ lưu SHA-256. CSRF token gắn phiên, so sánh
  thời gian hằng (`CryptographicOperations.FixedTimeEquals`).
- Không log secret, token, hash token, mật khẩu.

## Thời gian
- Dùng `TimeProvider` (đăng ký `TimeProvider.System`), không `DateTime.UtcNow` trực tiếp — test dùng `FakeTimeProvider`.

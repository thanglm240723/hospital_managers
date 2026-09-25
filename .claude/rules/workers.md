---
paths:
  - "**/*Worker*.cs"
  - "**/*Outbox*.cs"
  - "**/*Processor*.cs"
  - "**/*BackgroundService*.cs"
---
# Quy tắc worker nền và xử lý bất đồng bộ

- MVP chạy một API + một worker, nhưng worker phải đúng khi hai instance cùng chạy: claim việc bằng khóa DB
  (`FOR UPDATE SKIP LOCKED` hoặc cập nhật có điều kiện + claim token), không dựa vào khóa trong bộ nhớ.
- Outbox (đặc tả §11.3): INSERT cùng transaction nghiệp vụ; worker chọn batch nhỏ theo `NextAttemptAtUtc, Id`,
  đặt `ClaimToken` + `LeaseUntilUtc` rồi **commit trước khi gửi**; hoàn tất chỉ `UPDATE` khi `Id` và `ClaimToken` khớp;
  gia hạn lease khi xử lý lâu; retry backoff có jitter; quá ngưỡng ⇒ `DeadLetter` + cảnh báo.
- Giao hàng ít nhất một lần ⇒ consumer/nhà cung cấp phải idempotent (dùng `MessageId`). Không tuyên bố exactly-once.
- Payload Outbox chỉ tham chiếu resource (Id), không chứa bệnh án, token hay URL bí mật.
- Tôn trọng `stoppingToken`: ngừng nhận việc mới khi shutdown, không nuốt `OperationCanceledException`.
- Lỗi một vòng lặp không được làm chết worker: log (không PHI) rồi chờ vòng sau.
- Không giữ transaction DB khi gọi Redis, cloud storage, SMS/email.
- Tác vụ định kỳ (hủy chỉ định chưa thu tiền, dọn upload hết hạn, đối soát tệp) phải an toàn khi chạy lại và khi hai instance cùng chạy.
- Mẫu tham khảo đang có: `Infrastructure/Caching/CacheInvalidationWorker.cs` + `CacheInvalidationProcessor.cs`.

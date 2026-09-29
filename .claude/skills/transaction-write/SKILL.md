---
name: transaction-write
description: Thiết kế/review một Command ghi dữ liệu có transaction, khóa hàng, advisory lock, nhiều bước lưu, idempotency (Idempotency-Key), việc sau commit (xóa cache, Outbox, thông báo), retry hoặc tranh chấp đồng thời trong HMS.
---
1. **Chọn đúng một cách quản transaction** (không có behavior transaction trong pipeline):
   - một `SaveChangesAsync` — ghi đơn giản, không cần khóa;
   - `await using var tx = await _unitOfWork.BeginTransactionAsync(ct)` — cần khóa, nhiều bước, hoặc phải commit trước khi trả lỗi;
   - không ghi.
2. **Khóa**: lấy khóa **trước** khi nạp dữ liệu dùng để quyết định; nạp lại sau khóa. Thứ tự thống nhất (Id tăng dần;
   `ServiceOrder` trước `Invoice`; đợt trước giường). Bất biến toàn cục (còn ≥ 1 admin) ⇒ `pg_advisory_xact_lock`.
   Cập nhật có điều kiện (`… WHERE Status = 'Ready'`) phải kiểm tra số dòng ảnh hưởng = 1.
3. **Ràng buộc CSDL là tuyến cuối**: unique/partial unique index, CHECK, FK. Vi phạm unique do tranh chấp ⇒
   `Result.Failure` với `Error` loại conflict (409) có mã ổn định.
4. **Idempotency** (§11.1) cho cấp số, gọi lượt, nhập viện, gán/chuyển giường, thu tiền, cấp phát, xác nhận kết quả:
   INSERT key (`OrganizationId`, `ActorId`, `OperationType`, `IdempotencyKey`) + hash request trong **cùng** transaction với
   business write; lưu response tối thiểu. Trùng key khác hash ⇒ 409 `idempotency_key_reused`; đang chạy ⇒ 409
   `operation_in_progress` + `Retry-After`; replay kiểm tra lại quyền. Khóa nghiệp vụ lâu dài (`OperationId` unique) giữ riêng.
5. **Không gọi ngoài trong transaction**: Redis, HTTP, storage, SMS/email. Sau commit:
   - xóa cache: `ICacheInvalidator.Invalidate…` trước commit → `FlushAsync` sau commit (worker retry phần còn sót);
   - thông báo/tích hợp: Outbox INSERT cùng transaction, worker gửi (rule `workers.md`).
6. **Kết quả phải tồn tại dù request lỗi** (thu hồi family khi reuse, audit thất bại): commit rồi mới `return Result.Failure(...)`.
7. **Retry**: chỉ cả transaction với DbContext mới cho `40P01`/`40001` khi use case an toàn; không `EnableRetryOnFailure`
   bao ngoài transaction tự mở; không retry mù lỗi nghiệp vụ.
8. **Xác định hành vi khi**: handler lỗi giữa chừng, commit thành công nhưng việc sau commit lỗi, request trùng, client retry,
   mất response.
9. **Test** (integration, Testcontainers): commit, rollback khi lỗi giữa chừng (inject lỗi), không có side effect khi lỗi,
   retry cùng key trả cùng kết quả, N request đồng thời ⇒ đúng một thành công.

Mẫu tham khảo: `RefreshSessionCommandHandler` (khóa family/token, commit thu hồi trước khi trả 401) và luồng khóa người dùng
(advisory lock + khóa phiên + xóa cache sau commit). Bản đã chạy nằm trong lịch sử git (khung cũ, `ARCHITECTURE.md` §9);
bản khung mới đang được viết lại.

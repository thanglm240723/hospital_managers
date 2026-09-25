---
paths:
  - "**/CleanArchCqrs.Domain/**/*.cs"
  - "**/CleanArchCqrs.Application/**/*.cs"
  - "**/CleanArchCqrs.Infrastructure/**/*.cs"
  - "**/CleanArchCqrs.API/Controllers/**/*.cs"
---
# HMS — Bất biến nghiệp vụ (P0)

Nguồn: `Dac_ta_nghiep_vu_v2.0.docx` (NV/CFG/AT/OPEN) và `Dac_ta_ky_thuat_v3.1.docx` (§ trong ngoặc).
Rule này chỉ giữ những bất biến dễ bị phá nhất khi code; chi tiết đọc trong đặc tả. Mục `OPEN-xx` chưa chốt thì
**không tự chọn** công thức/giá trị — đánh dấu `CẦN_XÁC_NHẬN`.

## Chung
1. Không có đặt lịch: không entity `Appointment`, không API tạo lịch/giữ slot. Chỉ tiếp nhận và lấy số trực tiếp (NV-01, §5).
2. Lượt khám, phiếu chờ, chỉ định, kết quả, hóa đơn, cấp phát có **vòng đời riêng**, liên kết bằng Id nội bộ. Trạng thái
   chuyên môn, tài chính, kết quả, tệp là trường/bảng độc lập — không gộp enum tổ hợp (§2.2).
3. Enum trạng thái cố định bằng mã số + nhãn; không dùng chuỗi giao diện làm quy tắc. Chỉ cho chuyển trạng thái có trong
   bảng ánh xạ của BRD; aggregate từ chối mọi chuyển khác.
4. `EncounterRef` = đúng một trong `OutpatientEncounterId`/`InpatientEncounterId` (CHECK) + FK. `PatientId` suy ra từ đợt,
   không tin client. Dịch vụ/tài liệu liên quan phải cùng tổ chức và người bệnh (§2.2).
5. Không soft delete bệnh án/hóa đơn đã xác nhận. Dùng trạng thái hủy, bản sửa, bản thay thế có lý do. Danh mục ngừng dùng
   và tài khoản khóa qua `IsActive`. `User` không có `IsDeleted` (§2.2).
6. Không tin cờ từ client (`Paid`, `Emergency`, `BranchId`, quyền): đọc từ nguồn có thẩm quyền trong DB.

## Quyền và audit (§3)
7. Quyết định truy cập theo thứ tự: tài khoản hoạt động → quyền hành động → tổ chức/cơ sở → phân công hoặc grant còn hiệu lực
   → phạm vi dữ liệu. Từ chối mặc định. Cùng khoa hay Admin **không** tự có quyền đọc toàn bệnh viện.
8. Quyền theo tài nguyên áp dụng cho command, query, tìm kiếm, export và tải tệp; lọc trước phân trang và `COUNT`.
9. Grant cấp cứu mặc định 30 phút (OPEN-04 chốt vai trò), phát cảnh báo hậu kiểm.
10. Dữ liệu nhạy cảm: ghi audit bền vững **trước** khi trả; ghi audit lỗi thì không trả dữ liệu (`IAuditedRequest`).
11. Luôn còn ít nhất một admin đang hoạt động (`AdminSafety`, advisory lock).

## Tiếp nhận và hàng chờ (§5, NV-06..12)
12. Cấp số trong một transaction: idempotency → tạo lượt khám → tăng bộ đếm `Queue` bằng `UPDATE … RETURNING` → tạo
    `QueueTicket` → audit/outbox → commit. Unique (`QueueId`, `BusinessDate`, `DisplayNumber`). Mã in chỉ để hiển thị.
13. Thao tác đổi thứ tự hàng chờ khóa hàng `Queue` trong transaction ngắn. Chuyển phòng khóa hàng nguồn/đích theo Id tăng dần.
14. Gọi lại sau CFG-02 (60 giây); sau lần gọi thứ ba cần xác nhận vắng hoặc hết khoảng chờ mới chuyển `Late`. Quay lại từ hàng
    trễ mất ưu tiên cũ, giữ số, `CallCount=0` cho vòng mới, giữ lịch sử `CallAttempt`.
15. Sự kiện realtime/polling chỉ báo "có thay đổi"; client nạp lại theo quyền.

## Khám, kết quả, đơn thuốc (§6, NV-13..25)
16. `ServiceOrder` có `ExecutionStatus`, `FinancialClearanceStatus`, `ResultStatus` độc lập. Thu tiền và tự hủy chỉ định
    chưa thu cùng khóa `ServiceOrder` (Id tăng dần) rồi `Invoice`; đã thu thì worker không tự hủy.
17. Phiên bản kết quả/bệnh án: `Confirmed` là bất biến; sửa tạo phiên bản mới giữ bản cũ + audit. Unique (`RecordId`, `VersionNo`).
    Nháp dùng concurrency lạc quan (`xmin`) — lệch ⇒ 412, không tự merge nội dung lâm sàng.
18. Xác nhận kết quả yêu cầu đủ trường và tệp bắt buộc ở trạng thái `Available`.
19. Cấp phát thuốc: tổng đã cấp không vượt số kê; mỗi lần cấp có `OperationId` unique; khóa đơn và dòng theo thứ tự thống nhất.
    Phần mềm ghi nhận quy trình, không tự quyết liều.

## Nội trú (§7, NV-26..30)
20. Một giường tối đa một phân công hoạt động và một đợt tối đa một giường hoạt động: partial unique index
    `WHERE "EndedAtUtc" IS NULL`. Người đang nằm xác định bằng `BedAssignment` hoạt động — không thêm `PatientId` lên `Bed`.
21. Gán giường: cập nhật có điều kiện `Ready → Occupied`, số dòng ảnh hưởng phải = 1. Chuyển giường khóa đợt rồi hai giường
    theo Id tăng dần. Xuất viện: đóng assignment, `Bed = Cleaning`, `Encounter = Discharged` cùng transaction.
22. Xuất viện chuyên môn không đồng nghĩa `Paid`/`Settled`; hóa đơn thiếu tiền không chặn xuất viện.
23. Tranh chấp (giường đã bị lấy) ⇒ 409 có mã lỗi, không retry vô hạn.

## Viện phí (§8, NV-31..36)
24. `InvoiceHistory` chỉ thêm: không `UPDATE`/`DELETE` sự kiện tiền; điều chỉnh bằng sự kiện mới tham chiếu sự kiện cũ, có lý do và quyền.
25. Số tiền `decimal` (`numeric(19,2)`), không `float`/`double`. Làm tròn từng dòng về 0 chữ số thập phân với
    `MidpointRounding.AwayFromZero` rồi mới cộng. Outstanding = max(C − T, 0); Excess = max(T − C, 0).
26. Chụp giá và % BHYT theo phiên bản tại thời điểm ghi dòng phí; không tính lại hóa đơn cũ theo bảng giá hiện tại.
27. Sự kiện thu/hoàn lưu `Amount` dương, hướng tiền phân biệt bằng `Type`. CHECK: tỷ lệ 0..100, giá ≥ 0, số lượng > 0.
28. Thu tiền cần `Idempotency-Key`; không gọi ngân hàng; không tự thử lại bước thu tiền vật lý. Quy tắc ngày giường/tạm ứng
    thuộc OPEN-03 — chưa có cấu hình thì không tự chọn công thức.

## Tệp (§9–10, NV-37..41)
29. Nội dung tệp ở object storage, metadata/phiên bản ở PostgreSQL; không lưu base64 trong bảng hay log.
30. Không có transaction chung DB + cloud: `UploadSession` + các bước lặp an toàn; chỉ `Available` sau khi server xác minh
    checksum/kích thước, quét an toàn và promote. `Available` không quay lại `Pending`; thay tệp tạo phiên bản mới.
31. Object key không chứa PHI; tên tệp gốc không dùng ghép đường dẫn. Tải xuống kiểm tra quyền + audit mỗi request.

## Giao dịch (§11)
32. Idempotency: key (`OrganizationId`, `ActorId`, `OperationType`, `IdempotencyKey`) nhận bằng unique constraint trong cùng
    transaction; khác hash ⇒ 409 `idempotency_key_reused`; đang xử lý ⇒ 409 `operation_in_progress` + `Retry-After`.
33. Không gọi cloud/SMS/email/Redis trong transaction; việc sau commit qua Outbox/bảng chờ.

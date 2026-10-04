# Spec — Cấp số khám, bảng gọi số và gọi lượt

Ngày 2026-10-03. Trạng thái: **đã duyệt**. Module: `ReceptionQueue`.
Nguồn: NV-02, NV-08 (đặc tả nghiệp vụ v2.0); TSD v3.1 §5.1 (cấp số, phiếu chờ), §11.1 (idempotency);
`../2026-09-30-auth-va-luong-kham-v2/luong-kham-va-ho-so.md` (thứ tự nhóm Quay lại → Ưu tiên → Thường, bác sĩ tự gọi lượt).

## 0. Quyết định đã chốt

1. **Không có kiosk tự lấy số.** Lễ tân cấp số khám khi tiếp nhận (đúng NV-02). Người bệnh xếp hàng quầy theo cách thủ công.
2. Phiếu số **in ở quầy**; mỗi phòng khám có **một màn hình TV riêng** hiện số đang gọi và các số kế tiếp.
3. TV đăng nhập bằng **tài khoản thiết bị** chỉ có quyền `queues.display`; dùng lại luồng đăng nhập/refresh hiện có.
4. Phiếu in và bảng TV **không chứa tên hay định danh người bệnh** — chỉ số, phòng, giờ cấp.
5. **Buổi tự động, mỗi ngày một buổi cho mỗi hàng**: lần cấp số đầu tiên trong ngày tự mở buổi; worker tự đóng theo giờ cấu hình.
   Mở lại sau khi đóng cần `queues.manage` và lý do.
6. TV cập nhật bằng **polling 3–5 giây**; chưa dùng SignalR.

### 0.1 Chỗ lệch đặc tả

- Không có. (Tự mở buổi là cách triển khai; điều kiện "hết giờ không cấp số mới trừ người có quyền mở lại và ghi lý do" vẫn giữ.)

### 0.2 Phụ thuộc

- Cần `Patient` (PatientRegistry) và `Encounter` tối thiểu để tạo lượt khám. Nếu chưa có, plan phải làm phần tối thiểu trước
  hoặc tách thành plan riêng — **CẦN_XÁC_NHẬN** thứ tự khi lập plan. → Đã chốt: plan riêng trước (mục 10).
- Danh mục phòng khám lấy từ cơ cấu tổ chức (Facilities) đã có; `Queue` tham chiếu phòng.

## 1. Mô hình dữ liệu

| Aggregate | Trường chính | Ràng buộc CSDL |
|---|---|---|
| `Queue` | `Id`, `RoomId`, `Name`, `IsActive` | unique `RoomId` |
| `QueueSession` | `Id`, `QueueId`, `BusinessDate` (date, giờ VN), `Status` (`Open`/`Closed`), `LastNumber`, `OpenedAt`, `ClosedAt`, `ReopenReason`, `xmin` | partial unique `(QueueId)` WHERE `Status='Open'`; unique `(QueueId, BusinessDate)` |
| `QueueTicket` | `Id`, `QueueSessionId`, `Number`, `EncounterId`, `Group` (`Return`/`Priority`/`Normal`), `Status` (`Waiting`/`Called`/`Serving`/`Done`/`Late`/`Transferred`), `EnteredGroupAt`, `CalledAt`, `CalledByUserId`, `PreviousTicketId`, `xmin` | unique `(QueueSessionId, Number)`; partial unique `(EncounterId)` WHERE trạng thái đang hiệu lực |

- `Group` và `Status` là hai trường độc lập. Thời điểm lưu UTC; `BusinessDate` tính bằng `TimeProvider` theo Asia/Ho_Chi_Minh.
- Lịch sử chuyển/gọi lại nằm trong bảng sự kiện `QueueTicketEvents` (append-only).

## 2. Cấp số (lệnh tiếp nhận)

`POST /api/v1/encounters` — quyền `reception.register`, bắt buộc `Idempotency-Key`.

Trong **một transaction** (`IUnitOfWork.BeginTransactionAsync`):
1. Ghi nhận idempotency key (unique là điểm nhận quyền; trùng key + cùng hash → trả lại response cũ; khác hash → 422 `idempotency_key_reused`).
2. Lấy `QueueSession` của hàng cho `BusinessDate` hôm nay `FOR UPDATE`; chưa có → tạo `Open` (tranh chấp tạo đồng thời: bắt `23505`
   rồi đọc lại trong cùng lệnh, không retry mù). Đã `Closed` → 412 `queue_session_closed`.
3. `LastNumber += 1`, tạo `Encounter` + `QueueTicket(Waiting, Group)`; `PatientId` lấy từ hồ sơ, không tin client.
4. Commit. Response: `encounterId`, `ticketId`, `ticketNumber`, `roomName`, `issuedAt`.

Định dạng số hiển thị: 3 chữ số (`015`); >999 hiển thị đủ chữ số.

## 3. Gọi lượt

`POST /api/v1/queues/{id}/call-next` — quyền `queues.call`, `Idempotency-Key`.
- Chọn phiếu `Waiting` theo `Group` (Return → Priority → Normal) rồi `EnteredGroupAt` tăng dần,
  `FOR UPDATE SKIP LOCKED LIMIT 1`; chuyển `Called`, ghi `CalledByUserId`. Không có → 409 `queue_empty`.
- Bác sĩ đang có phiếu `Called/Serving` chưa xong ở hàng này → 409 `ticket_in_progress` (không chen ngang).
- `POST /api/v1/queue-tickets/{id}/recall` (gọi lại số đang gọi), `/mark-late` (vắng mặt → `Late`),
  `/return` (Late quay lại, **giữ số cũ**). Quyền `queues.call`; sai `xmin` → 412.
- Lọc hàng theo phạm vi phân công của bác sĩ (lớp 2) trong handler.

## 4. Đọc

| Route | Quyền | Trả về |
|---|---|---|
| `GET /api/v1/queues/{id}/board` | `queues.display` | `queueName`, `current` (số + nhóm), `next[]` (tối đa 5), `serverTime` — **không PHI** |
| `GET /api/v1/queues/{id}/tickets` | `queues.call` | danh sách chờ có tên người bệnh, phân trang chuẩn |

## 5. Buổi

- Worker `QueueSessionCloser` chạy định kỳ, đóng buổi `Open` khi quá giờ `ReceptionQueue:CloseTimeLocal` (cấu hình; mặc định **CẦN_XÁC_NHẬN**).
  Không hủy phiếu `Called/Serving`; phiếu `Waiting` còn lại vào danh sách điều phối (xử lý chi tiết thuộc plan NV-12, ngoài phạm vi).
- `POST /api/v1/queue-sessions/{id}/reopen` — quyền `queues.manage`, `reason` bắt buộc, ghi audit.

## 6. Quyền mới (`Domain/Identity/Permissions.cs`)

`reception.register`, `queues.call`, `queues.display`, `queues.manage`. Đồng bộ `permissionCodes.js` (bỏ nhãn DỰ_KIẾN cho hai mã đã có).
Seeder: vai trò Lễ tân ← `reception.register`; Bác sĩ ← `queues.call`; vai trò mới "Màn hình gọi số" ← `queues.display`.

## 7. Frontend

- **Tiếp nhận** (`/reception/intake/:patientId`): chọn phòng, nhóm Thường/Ưu tiên → "Tiếp nhận & cấp số" → hộp thoại số + **In phiếu**
  (`window.print()`, CSS khổ 80mm). Key sinh một lần mỗi lần bấm; timeout gửi lại **cùng key**; tắt nút khi đang gửi.
- **Bảng TV** (`/display/queue?queueId=`): toàn màn hình, không menu; số đang gọi cỡ lớn + danh sách kế tiếp; polling 3–5s;
  mất mạng giữ dữ liệu cũ và hiện chỉ báo "mất kết nối". Âm báo khi đổi số: tùy chọn.
- **Hàng chờ khám** (`/clinic/queue`): "Gọi lượt tiếp theo", "Gọi lại", "Vắng mặt", "Quay lại".
- Bật các khóa `reception.intake`, `clinic.queue` trong `availability.js` khi API thật đã nghiệm thu.
- Gateway: thêm route `/api/v1/encounters/**`, `/api/v1/queues/**`, `/api/v1/queue-tickets/**`, `/api/v1/queue-sessions/**`.

## 8. Nghiệm thu và test

- Integration (Testcontainers): 50 lệnh cấp số đồng thời khác key → 50 số duy nhất liên tục; cùng key → một lượt;
  hai bác sĩ `call-next` đồng thời → không trùng phiếu; buổi đã đóng → 412; tạo buổi đồng thời → đúng một buổi.
- Unit: thứ tự chọn nhóm, chuyển trạng thái phiếu, validator.
- Jest: hộp thoại cấp số + gửi lại cùng key; bảng TV polling và mất kết nối; không render tên người bệnh trên bảng.

## 9. Ngoài phạm vi

Kiosk tự lấy số; hàng chờ quầy tiếp đón; SignalR; âm thanh đọc số; chuyển hàng/điều phối cuối buổi (NV-08, NV-12) — plan sau; thu phí khám.

## 10. Đã chốt khi duyệt (2026-10-03)

1. Giờ đóng buổi mặc định `ReceptionQueue:CloseTimeLocal = 17:00` (giờ VN), cấu hình được.
2. Patient/Encounter tối thiểu làm ở **plan riêng trước**; plan này giả định đã có `Patient` và `Encounter`.

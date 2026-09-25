---
name: object-storage
description: Thiết kế/review lưu trữ tệp bệnh án và cận lâm sàng của HMS (module Documents) — IFileStorage, quarantine và kho chính, UploadSession, quét an toàn, promote, tải xuống có kiểm tra quyền, đối soát và phục hồi. Đặc tả kỹ thuật §9–10, NV-37..41.
---
- **Ranh giới**: `IFileStorage` ở Application (`PutQuarantinedAsync`, `OpenReadAsync`, `GetPropertiesAsync`, `PromoteVersionAsync`,
  `DeleteTemporaryAsync`); contract dùng object key, version, checksum, stream. SDK nhà cung cấp chỉ ở Infrastructure.
  Adapter tham chiếu: Azure Blob; nhà cung cấp/vùng cuối cùng là `OPEN-01` — không hard-code.
- **Tách kho**: quarantine và clinical-private khác quyền; tắt public access; HTTPS; credential dịch vụ quyền tối thiểu từ secret store.
- **Metadata ở PostgreSQL**: `ClinicalDocument` (Id, EncounterRef, DocumentType, version liên quan, Status, StorageProvider,
  Container, ObjectKey, ObjectVersionId, SizeBytes, Sha256, LogicalDocumentId, VersionNo, …) và `UploadSession`
  (State, ExpiresAtUtc, ClaimToken, ErrorCode). Unique (`LogicalDocumentId`, `VersionNo`).
- **Không transaction chung DB + cloud**. Luồng: khởi tạo (kiểm quyền, `PendingUpload` + session) → stream có giới hạn vào
  quarantine → server đọc size/checksum thật → worker nhận lease, kiểm magic bytes + quét mã độc → promote đúng version
  đã quét, kiểm lại checksum → DB xác nhận `Available` + audit/outbox. Mỗi bước lặp an toàn; lỗi sau copy ⇒ retry finalize.
- **Không đánh dấu `Available`** chỉ vì trình duyệt báo upload xong; scanner lỗi ⇒ chờ/retry, không coi là sạch.
- **Lease**: finalize dùng trạng thái có điều kiện + claim token; worker mất lease không ghi đè kết quả worker mới.
- **Giới hạn** (CFG-04): PDF/JPEG/PNG, 20 MiB/tệp, quota theo đợt; từ chối archive/file thực thi; session hết hạn 24 giờ.
- **Tải xuống** qua API: kiểm quyền tài nguyên gốc → audit bền vững → stream; `Cache-Control: no-store`, `nosniff`,
  `Content-Disposition` an toàn. Signed URL không mặc định; nếu bật thì ngắn hạn, read-only, không log URL.
- **Object key** chứa UUID/mã nội bộ, không PHI; tên gốc chỉ để hiển thị.
- **Đối soát**: worker tìm metadata Pending quá hạn, object tạm mồ côi, `Available` mất object ⇒ cảnh báo + cờ integrity,
  không xóa tệp chính thức vì một lần đọc lỗi. Backup phải phục hồi đúng object version mà DB tham chiếu.
- **Test**: adapter test trên môi trường riêng với dữ liệu giả; inject lỗi từng bước (sau copy trước finalize, thay object sau quét).

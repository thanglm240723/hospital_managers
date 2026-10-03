# Spec khung: danh mục quyền toàn hệ thống và phân quyền hai lớp

Ngày: 2026-10-03. Trạng thái: **chờ người dùng review (bản 2 — đã sửa theo review lần 1, xem §13)**. Các quyết định ghi "đã chốt" đến từ hội thoại brainstorming ngày 2026-10-03. Đây là spec **khung**: chốt danh mục quyền, ma trận vai trò, mô hình lớp 2 và cơ chế thực thi. Hạ tầng lớp 2 được triển khai dần theo từng module (mỗi module có spec/plan riêng bám spec này).

## 1. Nguồn và phạm vi

- BRD `Dac_ta_nghiep_vu_v2.0.docx`: NV-01 (tổ chức nhiều cơ sở), NV-03 (bảng vai trò, ba điều kiện truy cập), NV-04 (phân công), NV-05 (truy cập khẩn cấp, audit), NV-12, NV-16, NV-26, NV-29, NV-34, NV-40; CFG-05; OPEN-04; AT-09, AT-10, AT-17.
- TSD `Dac_ta_ky_thuat_v3.1.docx` §3 (dòng 68, 80–93): `Authorize(actor, action, resource)`, `CareTeamAssignment`, `AccessGrant`, lọc trước phân trang, không cache quyền phân công.
- [Spec V2 auth](../2026-09-30-auth-va-luong-kham-v2/spec.md) và [luồng khám và hồ sơ](../2026-09-30-auth-va-luong-kham-v2/luong-kham-va-ho-so.md) (bác sĩ tự gọi lượt; sinh hiệu theo yêu cầu bác sĩ).
- Hiện trạng code (sau plan V2 05/06): lớp 1 đã có — `Permissions.cs` (8 mã IdentityAccess), `SystemRoles` (9 vai trò, `IsSystem`), `RolePermission`, `UserPermission`, policy `perm:<code>`, cache Redis `perm:{userId}` có invalidation, audit từ chối 403. Lớp 2 chưa có gì.

Ngoài phạm vi: triển khai cụ thể từng module nghiệp vụ; ngừng dùng/đổi tên mã quyền; MFA; gộp hồ sơ.

## 2. Quyết định đã chốt

1. Spec khung (phương án A): chốt toàn bộ danh mục mã quyền và ma trận vai trò ngay; mô hình lớp 2 chốt ở mức thực thể/cơ chế; bảng và code lớp 2 làm dần theo module.
2. **Kích hoạt theo use case**: mã quyền chỉ được thêm vào `Permissions.cs`, seed và gán mặc định khi có endpoint dùng nó. Danh mục §4 là cố định; module không tự đặt mã mới ngoài danh mục (thêm mã = sửa spec này).
3. **Gán mặc định một lần**: mỗi cặp (vai trò, quyền) chỉ được seeder áp đúng một lần, có ghi lịch sử; admin bỏ quyền thì seeder không gán lại. Quyền lõi của Admin vẫn luôn được bảo vệ.
4. **Nhiều cơ sở, chặn liên cơ sở**: có `Branch → Department → Room`; dữ liệu điều trị mang `BranchId`. Bản này **không có đường nào vượt cơ sở** (kể cả grant); quy tắc liên cơ sở là `CẦN_XÁC_NHẬN (OPEN-04)` và sẽ được thêm bằng sửa spec khi chốt.
5. **Ngoại trú hai bậc**: phân công buổi khám cho thấy dữ liệu tối thiểu; bác sĩ gọi/nhận phiếu thì hệ thống tạo `CareTeamAssignment` cho lượt khám đó.
6. **RBAC (lớp 1) + ReBAC/ABAC (lớp 2)**, thực thi theo hướng port ở Application + bộ lọc truy vấn ở Persistence (không dùng PostgreSQL RLS, không dùng resource authorization ở tầng web làm cơ chế chính).

## 3. Mô hình tổng thể

### 3.1 Hai lớp

**Lớp 1 — RBAC**: "được làm loại hành động X không?". Nguồn: `RolePermission` ∪ `UserPermission`. Kiểm trên route bằng `RequirePermission(...)` → thiếu thì 403. Giữ cache Redis có invalidation như hiện tại.

**Lớp 2 — ReBAC + ABAC**: "với tài nguyên cụ thể này thì sao?".

- Quan hệ (ReBAC):
  - `assigned_to → ClinicSession` — được phân công vào buổi khám/phòng;
  - `care_team(DutyRole) → OutpatientEncounter | InpatientEncounter` — phân công điều trị có thời hạn;
  - `member_of → Department` cùng tài nguyên `routed_to → Department` — việc chuyển đến bộ phận (chỉ định CLS, đơn thuốc đến khoa dược);
  - `access_grant → tài nguyên` — quyền tạm thời.
- Thuộc tính (ABAC): `BranchId`/`DepartmentId` của tài nguyên so với phạm vi làm việc của người dùng; hiệu lực thời gian; trạng thái/phiên bản tài nguyên (`Confirmed` không sửa, lịch sử chỉ đọc); mức nhạy cảm (che/bớt trường trên danh sách, NV-03).

### 3.2 Thứ tự quyết định

Từ chối mặc định; một bước sai là dừng (TSD dòng 82):

1. Tài khoản còn hoạt động.
2. Có quyền hành động (lớp 1).
3. Tài nguyên thuộc cơ sở người dùng được làm việc (ABAC). Bước này **không có ngoại lệ** trong bản này: grant không vượt được cơ sở.
4. Có quan hệ còn hiệu lực **đúng loại mà policy của cặp (hành động, loại tài nguyên) yêu cầu** (ReBAC, §7.2) **hoặc** có `AccessGrant` hợp lệ cho đúng hành động và tài nguyên đó (§6.5).
5. Hành động hợp với trạng thái/phiên bản dữ liệu (ABAC), gồm quy tắc sửa bệnh án cũ (§7.3).

### 3.3 Nguyên tắc

- Kết quả là **AND** của hai lớp. Lớp 2 không bao giờ sinh quyền hành động; `AccessGrant` chỉ thay thế điều kiện quan hệ ở bước 4, không thay bước 2 hay bước 3.
- Admin không có quyền lâm sàng trong ma trận → bị chặn ở lớp 1 khi đọc bệnh án ("quản trị hệ thống không đồng nghĩa quyền đọc bệnh án").
- Cùng khoa không đủ để đọc hồ sơ (NV-04, AT-09).
- **Dữ liệu trả về được quyết định theo bộ ba (hành động, loại tài nguyên, loại quan hệ)**, không theo một mức chung cho mọi tài nguyên. "Đầy đủ" chỉ có nghĩa đầy đủ *trong phạm vi tài nguyên được phép*: KTV qua chỉ định được chuyển đến khoa chỉ thấy chỉ định, kết quả và định danh tối thiểu của người bệnh; Dược sĩ chỉ thấy đơn thuốc và thông tin cần cấp phát; không ai được "đầy đủ bệnh án" chỉ vì có một quan hệ bất kỳ (§7.2).
- Ngoài phạm vi hoặc không tồn tại → **404** `resource_not_found`, cùng thông báo; thiếu quyền lớp 1 → **403**.
- Lớp 2 **luôn đọc DB**, không cache giữa các request (TSD dòng 89, AT-17).
- Thiếu policy cho một cặp (hành động, loại tài nguyên) → **từ chối** (§7.4).

## 4. Danh mục mã quyền

Quy ước: `<tài-nguyên>.<hành-động>`, tài nguyên kebab-case số nhiều, tài nguyên con nối bằng dấu chấm. Mã đã chốt không đổi tên, không xóa. Nhóm (`Group`) và mô tả tiếng Việt khai cùng mã trong `PermissionDefinition`.

Cột "Lớp 2": **—** không kiểm thêm; **CS** phạm vi cơ sở/khoa; **QH** quan hệ (phân công, việc được chuyển đến bộ phận, hoặc grant), luôn kèm CS.

### 4.1 IdentityAccess (đã kích hoạt)

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `users.read` / `users.create` / `users.activate` | Xem, tạo, khóa/mở khóa tài khoản | — |
| `users.roles.manage` / `users.permissions.manage` | Gán vai trò; cấp/thu hồi quyền lẻ | — |
| `roles.read` / `roles.manage` | Xem; tạo/đổi tên/sửa quyền vai trò | — |
| `permissions.read` | Xem danh mục quyền | — |

### 4.2 Cơ cấu, danh mục, cấu hình

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `facilities.read` / `facilities.manage` | Xem / quản lý cơ sở, khoa, phòng | — |
| `staff-profiles.read` / `staff-profiles.manage` | Hồ sơ nhân sự và cơ sở/khoa được làm việc | — |
| `catalog.read` / `catalog.manage` | Danh mục dịch vụ, thuốc, giá, nhóm BHYT | — |
| `settings.manage` | Cấu hình CFG-xx | — |

### 4.3 PatientRegistry

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `patients.read` / `patients.create` / `patients.update` | Hồ sơ hành chính, tìm kiếm, chống trùng | — (hồ sơ dùng chung toàn tổ chức, NV-01; danh sách chỉ trả trường hành chính) |

### 4.4 ReceptionQueue

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `encounters.register` | Tạo lượt khám, cấp số (NV-08) | CS |
| `clinic-sessions.read` / `clinic-sessions.manage` | Xem; mở/đóng/tạm dừng buổi khám (NV-12) | CS |
| `clinic-sessions.staff.assign` | Phân công nhân sự vào buổi khám | CS |
| `queue.read` | Xem hàng chờ | QH (buổi khám) **hoặc** CS cấp khoa nếu người dùng có `queue.transfer` (§7.2.1) |
| `queue.call` | Gọi số, ghi vắng; bác sĩ gọi phiếu khám = nhận lượt (§6.4) | QH (buổi khám, đúng `DutyRole` của công đoạn) |
| `queue.transfer` | Điều chuyển phiếu đang chờ/trễ | CS cấp khoa trên **cả** buổi nguồn và buổi đích (§7.2.1) |
| `queue.display` | Bảng gọi số (tên được che) | CS cấp khoa/phòng của tài khoản thiết bị (§6.2) |

### 4.5 Clinical — ngoại trú

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `vitals.read` / `vitals.record` | Xem / ghi sinh hiệu | QH (buổi khám hoặc đợt nội trú) |
| `encounters.read` | Đọc bệnh án lượt khám | QH (phân công điều trị của **chính lượt đó**) |
| `encounters.examine` | Ghi nháp bệnh án của lượt đã nhận | QH (phân công điều trị của chính lượt đó). Việc *nhận lượt* đi qua `queue.call` (§6.4) |
| `encounters.confirm` | Xác nhận bệnh án | QH (như trên) |
| `encounters.amend` | Bản sửa bổ sung sau xác nhận | QH đang hiệu lực **của chính lượt chứa phiên bản cần sửa**, **hoặc** grant `RecordCompletion` cho chính lượt đó (§7.3; NV-04, NV-19) |
| `patient-history.read` | Đọc lịch sử đã xác nhận của người bệnh | QH (đang điều trị, `HistoryScope=ConfirmedHistory`) |
| `orders.read` / `orders.create` | Xem / ra chỉ định CLS | QH |
| `orders.cancel` | Hủy chỉ định chưa thu tiền, chưa thực hiện (NV-16) | CS |
| `results.read` / `results.record` / `results.confirm` | Kết quả CLS | QH (bác sĩ: lượt khám; KTV: chỉ định chuyển đến khoa) |
| `prescriptions.read` / `prescriptions.create` | Đơn thuốc | QH |
| `admission-requests.create` | Chỉ định nhập viện | QH |

### 4.6 Dược

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `dispensing.read` / `dispensing.record` | Xem đơn đến dược; ghi nhận cấp phát (NV-23) | QH (đơn chuyển đến khoa dược) |

### 4.7 Inpatient

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `inpatient.admit` | Hoàn tất hành chính nhập viện (NV-26) | CS |
| `inpatient.read` | Đọc đợt nội trú | QH |
| `care-team.assign` | Phân công ê-kíp nội trú, bàn giao | CS |
| `beds.read` / `beds.assign` / `beds.clean` / `beds.manage` | Xem; gán/chuyển; vệ sinh; bảo trì/ngừng (NV-29) | CS |
| `bed-waitlist.manage` | Danh sách chờ giường (NV-30) | CS |
| `medical-orders.create` / `medical-orders.execute` | Ra y lệnh / ghi thực hiện (NV-24) | QH |
| `inpatient.discharge` | Xuất viện | QH |

### 4.8 Billing

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `billing.read` | Xem viện phí (không có nội dung bệnh án) | CS |
| `billing.collect` / `billing.deposit` / `billing.refund` | Thu / tạm ứng / hoàn | CS |
| `billing.adjust` / `billing.settle` / `billing.reopen` | Điều chỉnh / quyết toán / mở lại quyết toán | CS |
| `billing.insurance-rate.override` | Sửa tỷ lệ BHYT theo ca, có lý do (NV-34) | CS |
| `billing.emergency-exception.approve` | Duyệt ngoại lệ tạm ứng cấp cứu (NV-26) | CS |

### 4.9 Documents

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `documents.read` / `documents.upload` / `documents.export` | Xem/tải; tải lên; in/xuất | QH theo bệnh án gốc (NV-40) |

### 4.10 Kiểm soát truy cập, audit, báo cáo

| Mã | Ý nghĩa | Lớp 2 |
|---|---|---|
| `access-grants.request-emergency` | Tự kích hoạt truy cập khẩn cấp (NV-05). **Chưa được kích hoạt** cho tới khi chốt OPEN-04/CFG-05 (§11) | CS |
| `access-grants.approve` | Duyệt quyền hoàn thiện hồ sơ và truy cập ngoại lệ | CS |
| `audit.read` | Xem nhật ký audit | CS |
| `reports.read` | Báo cáo NV-42 | CS |

Thông báo (NV-43) do hệ thống phát, không có quyền riêng.

## 5. Ma trận vai trò × quyền mặc định

"Mặc định": seeder gán theo §8 khi mã được kích hoạt. "Bổ sung": không gán mặc định; admin cấp lẻ (`UserPermission`) hoặc tạo vai trò tùy biến. Mọi vai trò dưới đây trừ Admin mặc định thêm `catalog.read`, `facilities.read`.

| Vai trò (`SystemRoles`) | Mặc định | Bổ sung |
|---|---|---|
| Lễ tân `receptionist` | `patients.read`, `patients.create`, `patients.update`, `encounters.register`, `clinic-sessions.read`, `queue.read`, `queue.transfer`, `inpatient.admit` | `billing.deposit`, `orders.cancel` |
| Điều dưỡng ngoại trú `outpatient-nurse` | `patients.read`, `clinic-sessions.read`, `queue.read`, `queue.call`, `vitals.read`, `vitals.record` | — |
| Bác sĩ `doctor` | `patients.read`, `clinic-sessions.read`, `queue.read`, `queue.call`, `vitals.read`, `encounters.read`, `encounters.examine`, `encounters.confirm`, `encounters.amend`, `patient-history.read`, `orders.read`, `orders.create`, `results.read`, `prescriptions.read`, `prescriptions.create`, `admission-requests.create`, `inpatient.read`, `medical-orders.create`, `inpatient.discharge`, `documents.read`, `documents.upload`, `documents.export` | — (¹) |
| Điều dưỡng nội trú `inpatient-nurse` | `patients.read`, `inpatient.read`, `beds.read`, `beds.assign`, `beds.clean`, `bed-waitlist.manage`, `medical-orders.execute`, `vitals.read`, `vitals.record`, `orders.read`, `results.read`, `prescriptions.read`, `documents.read`, `documents.upload` | — |
| KTV CLS `lab-technician` | `patients.read`, `orders.read`, `results.read`, `results.record`, `documents.read`, `documents.upload` | `results.confirm` |
| Dược sĩ `pharmacist` | `patients.read`, `prescriptions.read`, `dispensing.read`, `dispensing.record` | — |
| Thu ngân `cashier` | `patients.read`, `billing.read`, `billing.collect`, `billing.deposit`, `billing.refund`, `billing.settle` | `billing.adjust`, `billing.reopen`, `billing.insurance-rate.override`, `orders.cancel` |
| Quản lý chuyên môn `clinical-manager` | `patients.read`, `clinic-sessions.read`, `clinic-sessions.manage`, `clinic-sessions.staff.assign`, `queue.read`, `queue.transfer`, `care-team.assign`, `access-grants.approve`, `beds.read`, `beds.manage`, `bed-waitlist.manage`, `staff-profiles.read`, `audit.read`, `reports.read` | `billing.emergency-exception.approve` (²) |
| Admin `admin` | 8 quyền IdentityAccess (lõi, luôn bảo vệ), `facilities.read`, `facilities.manage`, `staff-profiles.read`, `staff-profiles.manage`, `catalog.read`, `catalog.manage`, `settings.manage`, `audit.read` | — |

(¹) `CẦN_XÁC_NHẬN (OPEN-04, CFG-05)`: vai trò được dùng truy cập khẩn cấp là cấu hình phải duyệt. `access-grants.request-emergency` **không** nằm trong ma trận mặc định và chưa được kích hoạt; khi chốt sẽ sửa spec (thêm vào ma trận của vai trò được duyệt) rồi mới kích hoạt.
(²) `CẦN_XÁC_NHẬN (NV-26)`: người có quyền xác nhận ngoại lệ tạm ứng cấp cứu.

Ghi chú:

- Quản lý chuyên môn **không** có `encounters.read` mặc định (BRD: "phạm vi cơ sở/khoa và quyền được cấp cụ thể").
- Admin và Thu ngân không có quyền lâm sàng.
- `queue.display` không thuộc vai trò hệ thống nào: thiết bị bảng gọi số dùng tài khoản riêng gắn vai trò tùy biến (do admin tạo) chỉ có `queue.display`; phạm vi theo §6.2.
- `encounters.amend` ở lớp 1 không đủ để sửa; lớp 2 theo §7.3 (AT-10).
- Lễ tân và Quản lý chuyên môn xem/chuyển hàng chờ theo **khoa trong `StaffWorkScope`**, không cần được gán vào từng buổi khám (§7.2.1). Bác sĩ và Điều dưỡng ngoại trú làm việc theo **phân công buổi khám**.

## 6. Mô hình dữ liệu lớp 2

Kiểu dữ liệu theo AGENTS.md: khóa `Guid` v7, thời điểm `DateTimeOffset` UTC, concurrency `xmin`, ràng buộc "một bản ghi đang hiệu lực" bằng partial unique index.

### 6.1 Cơ cấu tổ chức — module `Catalog`

- `Branch(Id, Code, Name, IsActive)` — một tổ chức ngầm định; triển khai seed một cơ sở.
- `Department(Id, BranchId, Code, Name, Kind, IsActive)` — `Kind`: `Clinical`, `Laboratory`, `Pharmacy`, `Billing`, `Administrative`.
- `Room(Id, DepartmentId, Code, Name, IsActive)`.
- Code duy nhất trong phạm vi cha (unique `(BranchId, Code)`, `(DepartmentId, Code)`); ngừng dùng bằng `IsActive`, không xóa.

### 6.2 Nhân sự và phạm vi làm việc — module `IdentityAccess`

- `StaffProfile(Id, UserId unique, StaffCode unique, IsActive)` — 1–1 với `User`. `DoctorId`/`StaffId` trong nghiệp vụ là `StaffProfileId` (TSD dòng 80).
- `StaffWorkScope(StaffProfileId, BranchId, DepartmentId)` — PK `(StaffProfileId, DepartmentId)`; `BranchId` suy từ `Department`, lưu để lọc. Dùng cho ABAC bước 3 (tập cơ sở) và ReBAC `member_of` (tập khoa).
- Người không có `StaffProfile` hoặc không có `StaffWorkScope` không qua được bước 3 với mọi tài nguyên CS/QH.
- **Tài khoản thiết bị** (bảng gọi số) cũng có `StaffProfile` (`StaffCode` tiền tố `DEV-`) và `StaffWorkScope` đúng khoa hiển thị. Vai trò của tài khoản này chỉ có `queue.display`, nên phạm vi khoa không mở thêm gì khác.

### 6.3 Phân công buổi khám — module `ReceptionQueue`

- `ClinicSessionStaff(Id, ClinicSessionId, StaffProfileId, DutyRole, AssignedAt, EndedAt, AssignedBy, EndedReason)`.
- `DutyRole`: `Doctor`, `OutpatientNurse`, `Coordinator`.
- Partial unique index `(ClinicSessionId, StaffProfileId) WHERE "EndedAt" IS NULL`.
- Kết thúc (NV-12, đóng buổi) bằng `EndedAt` + lý do, không xóa.
- Kết thúc phân công buổi khám **không** đóng các `CareTeamAssignment` của lượt khám đang mở mà bác sĩ đó đã nhận (§6.4). Ca đang phục vụ khi bác sĩ nghỉ/đóng buổi phải qua bàn giao (NV-12), không tự mất người phụ trách.

### 6.4 Phân công điều trị — module `IdentityAccess`

- `CareTeamAssignment(Id, UserId, EncounterKind, EncounterId, PatientId, BranchId, DutyRole, HistoryScope, ValidFrom, ValidTo, EndedReason, AssignedBy, RowVersion)`.
- `EncounterKind`: `Outpatient`, `Inpatient`. `DutyRole`: `AttendingDoctor`, `ConsultingDoctor`, `InpatientNurse`. `HistoryScope`: `None`, `ConfirmedHistory`.
- `PatientId`, `BranchId` do server suy từ đợt điều trị; không nhận từ client.
- Hành động được phép suy từ `DutyRole` qua policy trong code (`CareTeamPolicy`), không lưu danh sách hành động trên dòng.
- `ValidTo = null` nghĩa là còn hiệu lực đến khi đóng. Chuyển khoa/bàn giao/xuất viện: đóng dòng cũ, mở dòng mới trong cùng transaction với thay đổi nghiệp vụ (NV-04, NV-28).
- Partial unique index: một `AttendingDoctor` đang hiệu lực mỗi `(EncounterKind, EncounterId)`; không trùng `(EncounterId, UserId, DutyRole)` đang hiệu lực.
- Nội trú: tạo khi nhập viện/phân công ê-kíp (`care-team.assign`); đóng khi chuyển khoa/bàn giao/xuất viện.

#### 6.4.1 Vòng đời phân công ngoại trú

**Nhận lượt (tạo phân công).** Bác sĩ gọi một phiếu ở công đoạn khám (`queue.call`):

1. Lớp 2 kiểm **phân công buổi khám**: người gọi có `ClinicSessionStaff` đang hiệu lực với `DutyRole=Doctor` ở buổi chứa phiếu. Đây là quan hệ cho phép lần nhận đầu tiên, khi chưa có `CareTeamAssignment`.
2. Trong **cùng transaction** với việc đổi trạng thái phiếu: nếu lượt khám chưa có `AttendingDoctor` đang hiệu lực → tạo `CareTeamAssignment(AttendingDoctor, HistoryScope=ConfirmedHistory)` cho người gọi.
3. Nếu lượt đã có `AttendingDoctor` đang hiệu lực **là chính người gọi** (gọi lại phiếu quay lại sau sinh hiệu/CLS) → không tạo dòng mới.
4. Nếu lượt đã có `AttendingDoctor` đang hiệu lực **là người khác** → 409; muốn đổi bác sĩ phải qua chuyển phiếu/bàn giao.
5. Partial unique index (một `AttendingDoctor` đang hiệu lực mỗi lượt) là điểm chặn cuối khi hai bác sĩ gọi cùng lúc.

**Gọi phiếu sinh hiệu.** Điều dưỡng ngoại trú gọi phiếu ở công đoạn sinh hiệu (`queue.call`, `DutyRole=OutpatientNurse` của buổi) **không** tạo `CareTeamAssignment`. Ghi sinh hiệu (`vitals.record`) chỉ cần phân công buổi khám.

**Sau khi nhận.** Mọi thao tác khám (`encounters.read/examine/confirm`, `orders.*`, `prescriptions.*`, `results.read`, `patient-history.read`, `admission-requests.create`) kiểm **phân công điều trị của chính lượt đó**, không kiểm phân công buổi khám.

**Giữ quyền khi chờ.** Phân công còn hiệu lực trong suốt thời gian lượt khám còn mở, kể cả lúc người bệnh đang chờ sinh hiệu, chờ kết quả CLS hay ở nhóm quay lại.

**Mốc kết thúc.** Đóng phân công (`ValidTo = thời điểm`, `EndedReason`) trong cùng transaction với:

- lượt khám chuyển sang trạng thái kết thúc chuyên môn hoặc bị hủy (tên trạng thái chốt ở spec module Khám ngoại trú; **không** phải lúc xác nhận bệnh án, vì sau xác nhận vẫn có thể còn chỉ định chưa xong);
- chuyển phiếu/bàn giao sang bác sĩ khác (đóng cũ, mở mới cho bác sĩ nhận, cùng transaction).

**Sau khi kết thúc.** Hoàn thiện hồ sơ chỉ qua grant `RecordCompletion` có thời hạn, do người có `access-grants.approve` duyệt (NV-04). Không mở lại phân công cũ.

### 6.5 Quyền tạm thời — module `IdentityAccess`

- `AccessGrant(Id, GranteeUserId, Purpose, ResourceType, ResourceId, PatientId, BranchId, AllowedPermissionCodes, HistoryScope, GrantedBy, Reason, ValidFrom, ValidTo, RevokedAt, RevokedBy, RevokeReason)`.
- `Purpose`: `RecordCompletion` (hoàn thiện hồ sơ sau khi kết thúc điều trị), `Exceptional` (truy cập ngoài điều trị thông thường, phải duyệt trước), `Emergency` (chỉ dùng khi quyền khẩn cấp được kích hoạt, §11). **Không có** `CrossBranch` trong bản này.
- `ResourceType` chỉ gồm `OutpatientEncounter`, `InpatientEncounter`. Grant gắn một lượt/đợt cụ thể; không có grant "toàn bộ người bệnh".
- `BranchId` của grant = `BranchId` của lượt/đợt; người được cấp phải có cơ sở đó trong `StaffWorkScope` (bước 3 không có ngoại lệ).
- `ValidTo` luôn bắt buộc. Thu hồi bằng `RevokedAt`; không xóa.

**Điều kiện một grant thỏa bước 4** (tất cả phải đúng):

1. `GranteeUserId` là người gọi; `ValidFrom ≤ now < ValidTo`; `RevokedAt` null.
2. Hành động đang kiểm thuộc `AllowedPermissionCodes`; và người gọi **hiện vẫn có** quyền đó ở lớp 1 (AND, kiểm lại mỗi request).
3. Tài nguyên đang truy cập là chính `ResourceId`, hoặc là tài nguyên con của lượt/đợt đó (bệnh án, chỉ định, kết quả, đơn, tệp gắn với lượt/đợt).
4. Đọc lịch sử người bệnh qua grant chỉ khi `HistoryScope=ConfirmedHistory` và chỉ các phiên bản đã xác nhận.
5. Phiên bản/trạng thái tài nguyên hợp với hành động (bước 5).

**Tạo grant.**

- `AllowedPermissionCodes` khi tạo phải là tập con quyền lớp 1 hiện có của người được cấp, và thuộc danh sách hành động được phép cho `Purpose` đó (`RecordCompletion`: `encounters.read`, `encounters.amend`, `documents.read`, `documents.upload`; `Exceptional`: do người duyệt chọn, chỉ quyền đọc).
- `RecordCompletion`, `Exceptional`: người duyệt có `access-grants.approve` và cùng cơ sở; ghi audit; người duyệt không tự cấp cho mình.
- `Emergency`: người dùng tự kích hoạt, `ValidTo - ValidFrom ≤ CFG-05`, lý do bắt buộc, Outbox cảnh báo hậu kiểm — **chưa triển khai** cho tới khi §11 được chốt.

### 6.6 Việc chuyển đến bộ phận

Không thêm bảng. Module sở hữu phải có trường đích: `ServiceOrder.PerformingDepartmentId`, `Prescription.DispensingDepartmentId`. Bộ lọc dùng các trường này với tập khoa trong `StaffWorkScope`.

### 6.7 `BranchId` trên dữ liệu điều trị

- Có `BranchId`: `ClinicSession`, `OutpatientEncounter`, `QueueTicket`, `ServiceOrder`, `Prescription`, `InpatientEncounter`, `Invoice`, `Document`.
- Server sao từ bản ghi cha khi tạo; không bao giờ lấy từ client; không đổi sau khi tạo (chuyển cơ sở là nghiệp vụ riêng, ngoài phạm vi).
- `Patient` không có `BranchId`.

### 6.8 Sở hữu và ghi

- Bảng phân công/grant do `IdentityAccess`, `ReceptionQueue` sở hữu và ghi. Read service module khác được join đọc để lọc phạm vi; không ghi chéo bảng.
- Mọi thay đổi phân công/grant ghi audit (NV-05).

## 7. Cơ chế thực thi

### 7.1 Port ở Application (`Common/Authorization/`)

```csharp
public sealed record AccessScope(Guid UserId, Guid? StaffProfileId,
    IReadOnlySet<Guid> BranchIds, IReadOnlySet<Guid> DepartmentIds);

public interface IAccessContext
{
    Task<AccessScope> GetAsync(CancellationToken ct);   // đọc DB một lần/request, không cache liên request
}

public sealed record ResourceRef(string ResourceType, Guid Id);

/// Loại quan hệ đã thỏa bước 4 — module dùng cùng với hành động và loại tài nguyên để chọn DTO.
public enum RelationKind
{
    SessionStaff,        // ClinicSessionStaff đang hiệu lực
    DepartmentScope,     // khoa trong StaffWorkScope (lễ tân/điều phối với hàng chờ, theo policy)
    CareTeam,            // CareTeamAssignment của chính lượt/đợt
    DepartmentRouting,   // tài nguyên được chuyển đến khoa của người dùng
    Grant                // AccessGrant hợp lệ
}

public abstract record AccessDecision
{
    public sealed record Allowed(RelationKind Relation, string? DutyRole) : AccessDecision;
    public sealed record Denied(string Reason) : AccessDecision;
}

public interface IResourceAuthorizer
{
    Task<AccessDecision> AuthorizeAsync(string permissionCode, ResourceRef resource, CancellationToken ct);
}
```

- Implement ở Persistence. `IResourceAuthorizer` ghép các `IResourceScopePolicy` do từng module đăng ký, khóa theo cặp **(permissionCode, ResourceType)**. Thiếu policy cho cặp đang hỏi → `Denied("no_policy")` và log lỗi; không bao giờ mặc định cho phép.
- Thời điểm so hiệu lực lấy từ `TimeProvider`, truyền vào SQL làm tham số; không dùng `now()` của DB.

### 7.2 Policy theo (hành động, loại tài nguyên, quan hệ)

- Mỗi policy khai **rõ** những `RelationKind` được chấp nhận cho cặp (hành động, loại tài nguyên) của nó, và mỗi module có bảng ánh xạ (hành động, loại tài nguyên, `RelationKind`) → DTO được trả. Không có mức "đầy đủ" dùng chung.
- Ví dụ bắt buộc phải thể hiện trong policy của module tương ứng:
  - `encounters.read` × `OutpatientEncounter`: chỉ `CareTeam` (chính lượt) hoặc `Grant`.
  - `queue.read` × `QueueTicket`: `SessionStaff` hoặc `DepartmentScope` → DTO tối thiểu (số, tên, tuổi, lý do khám, sinh hiệu); không có nội dung bệnh án.
  - `results.record` × `ServiceOrder`: chỉ `DepartmentRouting` → chỉ định, kết quả, định danh tối thiểu để đối chiếu (NV-09).
  - `dispensing.read` × `Prescription`: chỉ `DepartmentRouting` → đơn thuốc và thông tin cần cấp phát.
- Danh sách/tìm kiếm: mỗi tài nguyên có extension lọc ở Persistence, ví dụ `IQueryable<OutpatientEncounter>.VisibleTo(AccessScope scope, string permission, DateTimeOffset now)`, sinh `WHERE` gồm điều kiện cơ sở và `EXISTS` tới đúng các nguồn quan hệ mà policy của cặp đó chấp nhận. Áp **trước** `COUNT`/`Skip`/`Take` trong cùng câu SQL; không lọc trong bộ nhớ.
- **Cùng một policy** cho danh sách, tìm kiếm, chi tiết, in/xuất và tải tệp của một loại tài nguyên: extension lọc và `IResourceScopePolicy` của cặp đó dùng chung một định nghĩa điều kiện (một nguồn sự thật), không viết hai lần.

#### 7.2.1 Hàng chờ (chốt cho module Tiếp nhận)

| Hành động | Ai | Quan hệ chấp nhận |
|---|---|---|
| `queue.call` (công đoạn khám/quay lại) | Bác sĩ | `SessionStaff` với `DutyRole=Doctor` ở buổi chứa phiếu |
| `queue.call` (công đoạn sinh hiệu) | Điều dưỡng ngoại trú | `SessionStaff` với `DutyRole=OutpatientNurse` |
| `queue.read` | Bác sĩ, điều dưỡng | `SessionStaff` (buổi được phân công) |
| `queue.read` | Người có `queue.transfer` (lễ tân, quản lý chuyên môn) | `DepartmentScope`: khoa của buổi thuộc `StaffWorkScope` |
| `queue.transfer` | Người có `queue.transfer` | `DepartmentScope` trên **cả** buổi nguồn và buổi đích (cùng cơ sở) |

- `queue.transfer` chỉ áp cho phiếu đang chờ/trễ. Phiếu đang phục vụ không chuyển bằng thao tác này; phải qua bàn giao ca đang khám (NV-12) — quy tắc bàn giao chốt ở spec module Tiếp nhận/Khám, và bàn giao đóng/mở `CareTeamAssignment` theo §6.4.1.
- Điều phối không cần được gán `Coordinator` vào từng buổi; `DutyRole=Coordinator` chỉ dùng khi buổi cần người điều phối riêng, không là điều kiện của bảng trên.

### 7.3 Command và quy tắc sửa bệnh án cũ

- Handler gọi `IResourceAuthorizer` **trong transaction, sau khi khóa hàng, trước khi ghi**.
- `PatientId`/`BranchId` lấy từ đợt điều trị đã tải.
- Policy `encounters.amend`: phiên bản cần sửa thuộc lượt/đợt **X**; chỉ cho phép khi người gọi có `CareTeamAssignment` đang hiệu lực **trên chính X** hoặc grant `RecordCompletion` trên chính X. Phân công ở một lượt/đợt mới hơn của cùng người bệnh **không bao giờ** cho phép sửa phiên bản thuộc lượt/đợt cũ (NV-04, NV-20, AT-10). `patient-history.read` chỉ trả phiên bản đã xác nhận và chỉ đọc.

### 7.4 Chống quên lọc

- Marker `IScopedRequest` cho query/command chạm tài nguyên có lớp 2; `IUnscopedRequest` cho request chủ ý không lọc.
- `IUnscopedRequest` **không tự đủ**: request phải nằm trong danh sách ngoại lệ được duyệt `ApprovedUnscopedRequests` (trong project test kiến trúc, mỗi mục có tên type và lý do). Thêm mục = thay đổi phải qua review.
- Architecture test (đỏ nếu vi phạm):
  - mọi `ICommand`/`IQuery` trong `Features/{ReceptionQueue, Clinical, Inpatient, Billing, Documents, Pharmacy}` implement **đúng một** marker;
  - không type nào implement cả hai;
  - mọi type implement `IUnscopedRequest` có trong `ApprovedUnscopedRequests`, và mọi mục trong danh sách còn tồn tại.
- Không có policy → từ chối (§7.1).
- Mỗi module có bộ integration test mẫu bắt buộc (§9.3), gồm chứng minh danh sách, chi tiết, export và tải tệp áp cùng phạm vi.

### 7.5 Từ chối và audit

- Lớp 2 từ chối → `Result.Failure` với lỗi not-found chung, mã `resource_not_found` → 404.
- **Audit từ chối phải sống sót qua rollback.** `IAuditWriter` hiện chỉ thêm vào unit of work đang mở, nên audit trong transaction nghiệp vụ sẽ mất khi rollback. Thêm port riêng:

  ```csharp
  public interface IDeniedAccessRecorder
  {
      // Ghi AuditRecord(Result=Denied) bằng DbContext và transaction RIÊNG, độc lập với unit of work của request.
      Task RecordAsync(string action, ResourceRef resource, string reason, CancellationToken ct);
  }
  ```

  - Implement ở Persistence bằng `IDbContextFactory<AppDbContext>` (context mới, `SaveChangesAsync` riêng), không dùng `IUnitOfWork` của request.
  - Handler gọi **sau khi** transaction nghiệp vụ đã dispose (rollback), rồi mới trả 404. Không gọi khi đang giữ khóa hàng.
  - Ghi audit lỗi → vẫn trả 404 (từ chối không đổi), log lỗi kèm correlation id; không trả dữ liệu.
  - Test bắt buộc: một command bị từ chối ở lớp 2 sau khi đã khóa hàng → không có thay đổi nghiệp vụ nào được ghi, **nhưng** `AuditRecord` Denied tồn tại.
- Danh sách không ghi từ chối (bị lọc). Danh sách dữ liệu nhạy cảm ghi bộ lọc và tập ID đã trả qua `IAuditedRequest`.

### 7.6 Che dữ liệu

Che ở server trong projection (ví dụ bảng gọi số "Nguyễn V. A"; danh sách hồ sơ cho Thu ngân không có trường lâm sàng). FE không tự che.

### 7.7 Truy cập khẩn cấp

**Chưa triển khai** trong bản này (§11). Thiết kế dự kiến khi được chốt: `RequestEmergencyAccessCommand` (transaction): kiểm lớp 1 `access-grants.request-emergency` + tài nguyên thuộc cơ sở người dùng → tạo `AccessGrant(Emergency)` (≤ CFG-05) → audit → Outbox cảnh báo → commit. Có hiệu lực ngay ở request kế tiếp; hết hạn/thu hồi tự mất hiệu lực, không cần job dọn.

### 7.8 Hiệu năng

Partial index cho dòng đang hiệu lực: `CareTeamAssignment(UserId, EncounterId)`, `CareTeamAssignment(UserId, PatientId)`, `ClinicSessionStaff(StaffProfileId, ClinicSessionId)`, `AccessGrant(GranteeUserId, ResourceId)`. Module đầu tiên dùng lớp 2 đo `EXPLAIN ANALYZE` trên dữ liệu giả.

### 7.9 Frontend

Chỉ ẩn/hiện theo quyền lớp 1 từ `/me`. Gặp 404 hiện "Không tìm thấy hoặc bạn không có quyền truy cập", không suy đoán lý do.

## 8. Seeder và vòng đời quyền

- Ma trận §5 viết đầy đủ một lần trong `Persistence/Seed/DefaultRolePermissions.cs`, khóa theo chuỗi mã; kèm `SupplementaryPermissions` (các mã chỉ cấp lẻ).
- Seeder chỉ áp mã **đã kích hoạt** (`Permissions.IsDefined`); mã chưa kích hoạt bị bỏ qua.
- Bảng mới `RolePermissionDefaults(RoleId, PermissionCode, AppliedAt)`, PK `(RoleId, PermissionCode)`.
- Mỗi lần khởi động, trong một transaction có `pg_advisory_xact_lock`:
  1. lấy cặp (vai trò, mã) trong ma trận, mã đã kích hoạt, chưa có trong `RolePermissionDefaults`;
  2. thêm `RolePermission` nếu chưa có;
  3. ghi dòng lịch sử;
  4. `InvalidatePermissions` cho thành viên vai trò;
  5. commit → `FlushAsync`.
- Backfill lần đầu: các cặp `RolePermission` đang có coi như đã áp.
- Quyền lõi Admin vẫn được bảo vệ bởi cơ chế hiện có (seeder + `admin_core_permissions_required`).
- Kích hoạt một module = thêm hằng vào `Permissions.cs`; không sửa ma trận.

## 9. Kiểm thử

### 9.1 Unit

- `CareTeamPolicy`: `DutyRole` → hành động.
- Điều kiện grant (§6.5): sai người, hết hạn, đã thu hồi, hành động ngoài `AllowedPermissionCodes`, tài nguyên khác lượt/đợt, mất quyền lớp 1 → đều từ chối.
- `IResourceAuthorizer` không có policy cho cặp (hành động, loại tài nguyên) → `Denied`.
- Seeder diff: cặp mới, bỏ qua mã chưa kích hoạt, không gán lại cặp đã có lịch sử.
- Mọi mã trong `Permissions.All` có mặt trong ma trận hoặc `SupplementaryPermissions`; mọi mã trong ma trận thuộc danh mục §4.

### 9.2 Architecture

Marker `IScopedRequest`/`IUnscopedRequest` và danh sách `ApprovedUnscopedRequests` (§7.4).

### 9.3 Integration (PostgreSQL thật)

Spec khung (mốc 1):

- seeder idempotent; admin bỏ quyền mặc định → restart không gán lại; hai instance khởi động đồng thời không áp trùng; thành viên thấy quyền mới ở request kế tiếp;
- `IDeniedAccessRecorder`: command bị từ chối sau khi khóa hàng → không có thay đổi nghiệp vụ, `AuditRecord` Denied vẫn tồn tại (§7.5).

Bộ mẫu bắt buộc cho mỗi module có lớp 2:

- cùng khoa, không phân công → 404 (AT-09);
- bác sĩ đang điều trị đọc lịch sử đúng phạm vi, không sửa được phiên bản thuộc lượt/đợt cũ (AT-10);
- thu hồi phân công → request kế tiếp bị chặn, kể cả tải tệp (AT-17);
- khác cơ sở → 404, kể cả khi có grant;
- grant hết hạn/thu hồi/sai hành động/sai tài nguyên → chặn; grant không vượt quyền lớp 1;
- danh sách, chi tiết, export và tải tệp của cùng tài nguyên cho cùng kết quả cho phép/từ chối;
- danh sách lọc trước `COUNT`, tổng đúng theo phạm vi;
- từ chối lớp 2 có `AuditRecord` Denied.

Thêm cho module Tiếp nhận/Khám ngoại trú (§6.4.1, §7.2.1):

- bác sĩ không có phân công buổi khám gọi phiếu → 404; có phân công → tạo đúng một `AttendingDoctor`;
- hai bác sĩ cùng gọi một phiếu → một thành công, một 409; không có hai `AttendingDoctor` đang hiệu lực;
- bác sĩ gọi lại phiếu quay lại → không tạo phân công mới;
- điều dưỡng gọi phiếu sinh hiệu → không tạo `CareTeamAssignment`;
- người bệnh đang chờ CLS → bác sĩ vẫn đọc/ghi được lượt; lượt kết thúc → mất quyền ở request kế tiếp;
- lễ tân xem/chuyển hàng chờ theo khoa trong `StaffWorkScope` mà không cần phân công buổi; chuyển sang buổi ở khoa ngoài phạm vi → 404;
- đóng buổi khám không làm mất phân công điều trị của lượt đang mở.

## 10. Lộ trình kích hoạt

Mỗi mốc có spec/plan riêng bám spec này.

1. **Nền lớp 2**: `Branch`/`Department`/`Room`; `StaffProfile`/`StaffWorkScope` (kể cả tài khoản thiết bị) và màn quản trị gán cơ sở/khoa cho nhân sự; `IAccessContext`; khung `IResourceAuthorizer` (đăng ký policy theo cặp, thiếu policy → từ chối) và `IDeniedAccessRecorder`; `RolePermissionDefaults` + seeder mới; marker + `ApprovedUnscopedRequests` + architecture test. Kích hoạt `facilities.*`, `staff-profiles.*` (`catalog.*` kích hoạt khi module danh mục dịch vụ/thuốc có endpoint — theo quyết định 2).
2. **Hồ sơ bệnh nhân**: `patients.*`.
3. **Tiếp nhận và hàng chờ**: `encounters.register`, `clinic-sessions.*`, `queue.*`; `ClinicSessionStaff`; policy đầu tiên của `IResourceAuthorizer` (§7.2.1); bảng gọi số.
4. **Khám ngoại trú**: `vitals.*`, `encounters.*`, `orders.*`, `prescriptions.*`, `patient-history.read`, `admission-requests.create`; `CareTeamAssignment` và vòng đời §6.4.1; `AccessGrant` (`RecordCompletion`, `Exceptional`), `access-grants.approve`.
5. CLS/kết quả → Dược → Viện phí → Nội trú → Tệp → Audit/Báo cáo.

Cờ availability FE chỉ bật khi màn có API đã nghiệm thu.

## 11. `CẦN_XÁC_NHẬN`

| Mục | Nội dung | Tạm thời trong spec |
|---|---|---|
| OPEN-04 | Quy tắc truy cập liên cơ sở | Chặn hoàn toàn; không có grant vượt cơ sở. Muốn làm việc ở cơ sở khác phải được thêm `StaffWorkScope` cơ sở đó. Khi chốt quy tắc: sửa §3.2 bước 3, thêm `Purpose` tương ứng, người duyệt và ca kiểm thử |
| OPEN-04 | Danh mục nhân sự được phân công | Theo `StaffProfile` + `StaffWorkScope` |
| OPEN-04, CFG-05 | Vai trò được dùng truy cập khẩn cấp | Chưa kích hoạt `access-grants.request-emergency`, không vai trò nào có mặc định; `Purpose=Emergency` chưa triển khai |
| NV-26 | Người duyệt ngoại lệ tạm ứng cấp cứu | Quyền bổ sung, gợi ý Quản lý chuyên môn |

## 12. Chỗ lệch so với TSD

| TSD | Spec này | Lý do |
|---|---|---|
| `CareTeamAssignment.AllowedActions` lưu trên dòng | Suy hành động từ `DutyRole` qua `CareTeamPolicy` | Đủ cho các vai trò điều trị BRD, tránh dữ liệu quyền phân tán; cần hành động riêng thì dùng `AccessGrant` |
| Dòng 89: không cache quyền theo TTL dài | Lớp 1 giữ cache Redis có invalidation (lệch đã ghi ở spec auth 2026-09-23 §0.1); lớp 2 không cache | Giữ quyết định đã duyệt; phần phân công đúng TSD |
| Dòng 80: "một vai trò chính được gán ban đầu" | Một người có thể có nhiều vai trò (đã có từ plan V2 06) | Hiện trạng đã duyệt |

## 13. Thay đổi theo review lần 1 (2026-10-03)

| # | Vấn đề review | Đã sửa |
|---|---|---|
| 1 | Chưa rõ quan hệ nào cho phép lần nhận lượt đầu tiên | §6.4.1: nhận lượt qua `queue.call` kiểm phân công buổi khám `DutyRole=Doctor`, tạo `CareTeamAssignment` cùng transaction; gọi lại không tạo trùng; người khác đang giữ → 409; điều dưỡng gọi sinh hiệu không tạo phân công |
| 2 | Lễ tân/điều phối có quyền hàng chờ nhưng không có quan hệ buổi khám | §7.2.1: phạm vi theo từng hành động; lễ tân/điều phối theo khoa trong `StaffWorkScope`; chuyển phiếu kiểm cả nguồn và đích; phiếu đang phục vụ phải bàn giao |
| 3 | `Minimal`/`Full` quá rộng | Bỏ `AccessLevel`; `AccessDecision` trả `RelationKind`; DTO theo (hành động, loại tài nguyên, quan hệ) §3.3, §7.2; điều kiện grant §6.5; quy tắc sửa bệnh án cũ §7.3 |
| 4 | Grant liên cơ sở mâu thuẫn bước 3; quyền khẩn cấp chưa chốt nhưng đã mặc định | Bước 3 không ngoại lệ; bỏ `CrossBranch`; bỏ `access-grants.request-emergency` khỏi ma trận, chưa kích hoạt (§2.4, §5, §6.5, §7.7, §11) |
| 5 | Audit từ chối mất khi rollback (`IAuditWriter` chỉ thêm vào unit of work) | §7.5: `IDeniedAccessRecorder` ghi bằng DbContext/transaction riêng sau rollback; test bắt buộc §9.3 |
| 6 | Marker có thể bị lách | §7.4: `ApprovedUnscopedRequests` có lý do, cấm hai marker, thiếu policy → từ chối; test cùng phạm vi cho danh sách/chi tiết/export/tải tệp |
| 7 | Vòng đời phân công ngoại trú chưa rõ | §6.4.1: giữ quyền khi chờ sinh hiệu/CLS; đóng khi lượt kết thúc chuyên môn/hủy hoặc bàn giao; sau kết thúc chỉ qua grant `RecordCompletion`; đóng buổi không đóng phân công điều trị (§6.3) |
| + | Tài khoản bảng gọi số thiếu phạm vi | §6.2: tài khoản thiết bị có `StaffProfile` + `StaffWorkScope` |
| + | `IResourceAuthorizer` đến muộn | §10: khung đưa vào mốc 1, policy đầu tiên ở mốc 3 |

Đổi tên `Purpose` `PostDischargeCompletion` → `RecordCompletion` vì dùng cho cả ngoại trú và nội trú.

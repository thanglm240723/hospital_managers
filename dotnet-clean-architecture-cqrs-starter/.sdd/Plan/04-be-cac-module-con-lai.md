# 04 — Backend: 4 module còn lại

Lặp lại đúng 9 bước của [03-be-module-patients.md](03-be-module-patients.md).
File này chỉ ghi **phần khác biệt** của từng module.

Thứ tự bắt buộc: **Doctors → Appointments → MedicalRecords → Billing** (module sau phụ thuộc module trước).

---

## 4.1 — Doctors

Gần như giống hệt Patients. Entity `Doctor`:

| Property | Ghi chú |
|---|---|
| `DoctorCode` | unique, `BS` + số thứ tự |
| `FullName`, `PhoneNumber`, `Email` | |
| `Specialty` | chuyên khoa — enum hoặc bảng riêng |
| `LicenseNumber` | số chứng chỉ hành nghề, unique |
| `Department` | khoa/phòng |
| `UserId` | `Guid?` — liên kết tới `User` để bác sĩ đăng nhập |
| `IsAvailable` | `bool` — còn nhận lịch hay không |

Use-case riêng: `GetAvailableDoctorsQuery(specialty, date)` — dùng cho màn đặt lịch bên FE.

⚠ `UserId` là khoá ngoại **lỏng** (không `HasForeignKey` cứng) — `User` thuộc bounded context Auth,
sau này tách service thì hai bảng không cùng DB nữa.

---

## 4.2 — Appointments

Module đầu tiên có **luật nghiệp vụ thật**. Làm chậm lại.

Entity `Appointment`: `PatientId`, `DoctorId`, `ScheduledAt` (UTC), `DurationMinutes`,
`Status`, `Reason`, `Notes`, `CheckedInAt?`, `CompletedAt?`, `CancelledAt?`, `CancellationReason?`

**→ `Domain/Enums/AppointmentStatus.cs`**

```
Scheduled → CheckedIn → InProgress → Completed
    ↓           ↓            ↓
 Cancelled  Cancelled    Cancelled
                          NoShow  (từ Scheduled, khi quá giờ)
```

⚠ Dùng **use-case theo động từ**, không dùng `UpdateAppointmentCommand` chung:

| Command | Chuyển trạng thái | Role |
|---|---|---|
| `ScheduleAppointmentCommand` | → `Scheduled` | Receptionist, Admin |
| `CheckInAppointmentCommand` | `Scheduled` → `CheckedIn` | Receptionist, Nurse |
| `StartAppointmentCommand` | `CheckedIn` → `InProgress` | Doctor |
| `CompleteAppointmentCommand` | `InProgress` → `Completed` | Doctor |
| `CancelAppointmentCommand` | bất kỳ → `Cancelled` | Receptionist, Admin |
| `RescheduleAppointmentCommand` | đổi `ScheduledAt`, giữ `Scheduled` | Receptionist, Admin |

**→ Tạo method `Appointment.CanTransitionTo(AppointmentStatus next)` trong entity.**
Mọi handler gọi nó trước, sai thì ném `BusinessRuleViolationException`.
⚠ Luật chuyển trạng thái thuộc **Domain**, không nằm rải rác trong các handler.

**Luật nghiệp vụ trong `ScheduleAppointmentCommandHandler`:**
1. Patient tồn tại, Doctor tồn tại và `IsAvailable`.
2. `ScheduledAt` phải ở tương lai.
3. **Không trùng lịch bác sĩ** — không có appointment nào của doctor đó (status `Scheduled`/`CheckedIn`/`InProgress`)
   giao khoảng `[ScheduledAt, ScheduledAt + Duration)`.
4. Không trùng lịch bệnh nhân, cùng cách.

⚠ Check trùng lịch có **race condition**: 2 request đồng thời cùng qua bước 3.
Giai đoạn đầu chấp nhận được; muốn chắc thì thêm unique index có filter, hoặc `SELECT ... WITH (UPDLOCK)`.
Ghi chú lại trong code bằng comment, đừng để người sau tưởng đã kín.

Query cần có: `GetAppointmentsByDoctorQuery(doctorId, fromDate, toDate)`,
`GetAppointmentsByPatientQuery(patientId)`, `GetTodayAppointmentsQuery()`.

Index: `(DoctorId, ScheduledAt)`, `(PatientId, ScheduledAt)`, `(Status, ScheduledAt)`.

---

## 4.3 — MedicalRecords

⚠ **Controller phải là `[Route("api/medical-records")]` viết tay** — `[Route("api/[controller]")]`
sinh ra `api/medicalrecords`, không khớp route gateway.

Entity `MedicalRecord`: `PatientId`, `DoctorId`, `AppointmentId?`, `VisitDate`,
`ChiefComplaint` (lý do khám), `Symptoms`, `Diagnosis`, `IcdCode?`, `TreatmentPlan`, `Notes?`

Entity con `Prescription` (đơn thuốc): `MedicalRecordId`, `MedicineName`, `Dosage`, `Frequency`,
`DurationDays`, `Instruction`

⚠ `Prescription` **không** phải aggregate root — không có `IPrescriptionRepository`.
Truy cập qua `MedicalRecord`, EF cấu hình `.HasMany(r => r.Prescriptions).WithOne().OnDelete(Cascade)`.

**Quy tắc bảo mật — quan trọng nhất module này:**

| Role | Được xem |
|---|---|
| Admin | tất cả |
| Doctor | tất cả (cần hội chẩn) |
| Nurse | tất cả, **chỉ đọc** |
| Receptionist | ❌ không được xem hồ sơ bệnh án |
| Accountant | ❌ không được xem |

⚠ **Hồ sơ bệnh án không được sửa sau khi ký.** Thêm `IsFinalized` + `FinalizedAt`.
`FinalizeMedicalRecordCommand` khoá bản ghi; mọi command sửa phải chặn nếu `IsFinalized == true`.
Cần đính chính thì tạo bản ghi bổ sung (`AmendmentOf` trỏ về bản gốc), không sửa đè.

---

## 4.4 — Billing

⚠ Controller `[Route("api/billing")]` viết tay (số ít, không phải `Billings`).

Entity `Invoice`: `InvoiceNumber` (unique), `PatientId`, `AppointmentId?`, `Status`,
`IssuedAt`, `PaidAt?`, `SubTotal`, `InsuranceCoverage`, `TotalAmount`, `Note?`

Entity con `InvoiceItem`: `InvoiceId`, `Description`, `Quantity`, `UnitPrice`, `LineTotal`

**→ `Domain/Enums/InvoiceStatus.cs`**: `Draft → Issued → Paid`, nhánh `Cancelled`

⚠ **Tiền dùng `decimal`, không bao giờ dùng `double`/`float`.**
EF: `.HasPrecision(18, 2)` cho mọi cột tiền.

⚠ **`TotalAmount` phải tính trong Domain**, không nhận từ client:

```csharp
SubTotal = Items.Sum(i => i.Quantity * i.UnitPrice);
TotalAmount = SubTotal - InsuranceCoverage;
```

Nhận `TotalAmount` từ request là lỗ hổng — client sửa được số tiền phải trả.

⚠ **Hoá đơn đã `Issued` thì không được sửa item.** Sai thì `Cancelled` rồi phát hành hoá đơn mới.

Use-case: `CreateDraftInvoiceCommand`, `AddInvoiceItemCommand`, `IssueInvoiceCommand`,
`RecordPaymentCommand`, `CancelInvoiceCommand`, `GetInvoicesByPatientQuery`, `GetUnpaidInvoicesQuery`.

Role: `Accountant` + `Admin` toàn quyền; `Receptionist` chỉ đọc; `Doctor`/`Nurse` không truy cập.

---

## Checklist chung mỗi module

- [ ] Entity kế thừa `AggregateRoot<Guid>` (aggregate root) hoặc `Entity<Guid>`; khởi tạo qua factory, không `new`
- [ ] Repository interface ở **Domain**, implementation ở **Infrastructure**
- [ ] DTO list (Summary) tách khỏi DTO chi tiết
- [ ] Validator chỉ kiểm hình dạng; luật nghiệp vụ ở handler/entity
- [ ] `IEntityTypeConfiguration` có index + `HasQueryFilter(!IsDeleted)`
- [ ] Repository: `AsNoTracking` cho read, `OrderBy` trước `Skip/Take`
- [ ] Controller `[Authorize]` cấp class, `[Authorize(Roles=...)]` từng action
- [ ] Đăng ký repository vào `InfrastructureServiceExtensions`
- [ ] Thêm `DbSet` vào `AppDbContext`
- [ ] `dotnet ef migrations add Add<Module>` + `database update`
- [ ] Test qua **gateway `:5100`**, không test thẳng `:5289`

✓ **Xong phase 4 khi:** đi hết luồng thật — tiếp nhận bệnh nhân → đặt lịch → check-in →
bác sĩ khám và ghi hồ sơ → xuất hoá đơn → thanh toán, mỗi bước dùng đúng role của bước đó.

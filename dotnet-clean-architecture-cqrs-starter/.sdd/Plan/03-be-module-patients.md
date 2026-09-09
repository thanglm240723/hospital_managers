# 03 — Backend: module Patients (slice mẫu)

Đây là **khuôn mẫu**. Làm kỹ module này, 4 module sau chỉ là lặp lại công thức.

## Cây file đầy đủ của một module

```
Domain/
├── Entities/Patient.cs
└── Interfaces/IPatientRepository.cs

Application/Patients/
├── Commands/
│   ├── CreatePatient/     CreatePatientCommand.cs + Handler
│   ├── UpdatePatient/     UpdatePatientCommand.cs + Handler
│   └── DeletePatient/     DeletePatientCommand.cs + Handler
├── Queries/
│   ├── GetPatient/        GetPatientQuery.cs + Handler
│   └── GetPatients/       GetPatientsQuery.cs + Handler
├── Validators/
│   ├── CreatePatientCommandValidator.cs
│   └── UpdatePatientCommandValidator.cs
└── Models/
    ├── PatientDto.cs
    └── PatientSummaryDto.cs

Infrastructure/
├── Persistence/Configurations/PatientConfiguration.cs
└── Repositories/PatientRepository.cs

API/
└── Controllers/PatientsController.cs
```

**14 file cho 1 module.** Nghe nhiều nhưng mỗi file rất ngắn — đó là cái giá của CQRS.

## Bước 3.1 — Entity

**→ Tạo `Domain/Patients/Patient.cs`** — kế thừa `AggregateRoot<Guid>`

| Property | Kiểu | Ràng buộc |
|---|---|---|
| `PatientCode` | `string` | unique, sinh tự động `BN` + `yyyyMM` + số thứ tự |
| `FullName` | `string` | bắt buộc, ≤ 200 |
| `DateOfBirth` | `DateOnly` | không được ở tương lai |
| `Gender` | `Gender` (enum) | Male / Female / Other |
| `PhoneNumber` | `string` | bắt buộc, 10 số |
| `IdentityNumber` | `string?` | CCCD, unique nếu có |
| `InsuranceNumber` | `string?` | số thẻ BHYT |
| `Address` | `string?` | ≤ 500 |
| `BloodType` | `string?` | A / B / AB / O ± |
| `Allergies` | `string?` | ⚠ trường sống còn, hiển thị nổi bật ở FE |
| `EmergencyContactName` | `string?` | |
| `EmergencyContactPhone` | `string?` | |

⚠ `DateOnly` chứ không `DateTime` — ngày sinh không có giờ, tránh lệch múi giờ.
⚠ Enum `Gender` đặt ở `Domain/Enums/Gender.cs`, file riêng.

## Bước 3.2 — Repository interface (Domain)

```csharp
public interface IPatientRepository
{
    Task<Patient?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Patient?> GetByCodeAsync(string patientCode, CancellationToken ct = default);
    Task<(IReadOnlyList<Patient> Items, int TotalCount)> GetPagedAsync(
        int pageNumber, int pageSize, string? searchTerm, CancellationToken ct = default);
    Task<bool> ExistsAsync(Guid id, CancellationToken ct = default);
    Task<bool> IdentityNumberExistsAsync(string identityNumber, Guid? excludeId, CancellationToken ct = default);
    Task AddAsync(Patient patient, CancellationToken ct = default);
    Task UpdateAsync(Patient patient, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);
}
```

⚠ Trả tuple `(Items, TotalCount)` chứ **không** trả `PagedResult<T>` — `PagedResult` là model của
Application, Domain không được biết.
⚠ `excludeId` trong `IdentityNumberExistsAsync` để lúc update không tự báo trùng với chính nó.

## Bước 3.3 — Command + Handler

**`CreatePatientCommand`** — record positional, `IRequest<Guid>`:

```csharp
public record CreatePatientCommand(
    string FullName,
    DateOnly DateOfBirth,
    Gender Gender,
    string PhoneNumber,
    string? IdentityNumber,
    string? InsuranceNumber,
    string? Address,
    string? BloodType,
    string? Allergies,
    string? EmergencyContactName,
    string? EmergencyContactPhone
) : IRequest<Guid>;
```

**`CreatePatientCommandHandler` — logic:**
1. `IdentityNumber` có giá trị → check trùng, trùng thì ném `BusinessRuleViolationException`.
2. Sinh `PatientCode`.
3. `Patient.Create(...)` (factory, không dùng `new`), `AddAsync`, trả `patient.Id`.

⚠ **Không** set `CreatedAt` / `UpdatedAt` trong handler — entity tự quản lý qua factory `Create()` và `Touch()`.

**`DeletePatientCommandHandler`:** kiểm tra tồn tại trước, không thấy thì ném `NotFoundException`.
Xoá là soft delete, DbContext tự chuyển.

## Bước 3.4 — Query + Handler

**`GetPatientsQuery`** kế thừa `PagedQuery`, `IRequest<PagedResult<PatientSummaryDto>>`.

⚠ **Query list trả `PatientSummaryDto`** (Id, PatientCode, FullName, DateOfBirth, Gender, PhoneNumber)
— nhẹ, an toàn. **Query chi tiết mới trả `PatientDto`** đầy đủ. Đừng dùng chung 1 DTO cho cả hai:
danh sách 20 bệnh nhân không cần kéo theo tiền sử dị ứng và liên hệ khẩn cấp.

`GetPatientQueryHandler`: không thấy → ném `NotFoundException`, **không** trả `null`.
Để middleware map 404, controller khỏi phải tự check.

## Bước 3.5 — Validator

`CreatePatientCommandValidator : AbstractValidator<CreatePatientCommand>`

```
FullName          NotEmpty, MaximumLength(200)
DateOfBirth       NotEmpty, phải <= hôm nay
Gender            IsInEnum
PhoneNumber       NotEmpty, Matches(@"^0\d{9}$")
IdentityNumber    khi có giá trị: Matches(@"^\d{12}$")
BloodType         khi có giá trị: Must(nằm trong danh sách hợp lệ)
```

⚠ Validator chỉ kiểm tra **hình dạng dữ liệu**. Luật nghiệp vụ (trùng CCCD, bệnh nhân đã có lịch hẹn
thì không cho xoá) nằm ở **handler**, không nhét vào validator — validator không truy cập được DB.

## Bước 3.6 — EF configuration

`PatientConfiguration : IEntityTypeConfiguration<Patient>`

```
ToTable("Patients")
HasKey(p => p.Id)
Property(PatientCode).IsRequired().HasMaxLength(20)
HasIndex(PatientCode).IsUnique()
HasIndex(IdentityNumber).IsUnique().HasFilter("[IdentityNumber] IS NOT NULL")
HasIndex(PhoneNumber)                          // tra cứu nhanh khi tiếp nhận
Property(FullName).IsRequired().HasMaxLength(200)
HasQueryFilter(p => !p.IsDeleted)
```

⚠ Unique index trên cột nullable phải có `HasFilter`, nếu không SQL Server coi nhiều `NULL` là trùng nhau.

## Bước 3.7 — Repository implementation

⚠ Search phải nối điều kiện **trước** khi `ToListAsync`, và `CountAsync` trên **cùng** query đã lọc:

```csharp
var query = _context.Patients.AsNoTracking();
if (!string.IsNullOrWhiteSpace(searchTerm))
    query = query.Where(p => p.FullName.Contains(searchTerm)
                          || p.PatientCode.Contains(searchTerm)
                          || p.PhoneNumber.Contains(searchTerm));

var total = await query.CountAsync(ct);
var items = await query.OrderByDescending(p => p.CreatedAt)
                       .Skip((pageNumber - 1) * pageSize).Take(pageSize)
                       .ToListAsync(ct);
```

⚠ Bắt buộc có `OrderBy` trước `Skip/Take` — không có thì thứ tự không xác định, phân trang lặp bản ghi.

## Bước 3.8 — Controller

`[ApiController] [Route("api/patients")] [Authorize]`

| Method | Route | Role được phép |
|---|---|---|
| GET | `` | Admin, Doctor, Nurse, Receptionist |
| GET | `{id:guid}` | Admin, Doctor, Nurse, Receptionist |
| POST | `` | Admin, Receptionist |
| PUT | `{id:guid}` | Admin, Receptionist |
| DELETE | `{id:guid}` | Admin |

Controller chỉ làm 3 việc: nhận request → `_mediator.Send(...)` → trả `Ok`/`CreatedAtAction`/`NoContent`.
**Không** if/else nghiệp vụ, **không** try/catch (middleware lo).

⚠ `PUT {id}` phải check `id != command.Id` → `BadRequest()`.
⚠ Mọi action nhận `CancellationToken cancellationToken = default` và truyền xuống `Send`.

## Bước 3.9 — Đăng ký & migration

- `InfrastructureServiceExtensions`: `services.AddScoped<IPatientRepository, PatientRepository>();`
- `AppDbContext`: thêm `public DbSet<Patient> Patients => Set<Patient>();`
- `dotnet ef migrations add AddPatients` → `database update`

✓ **Xong phase 3 khi:** qua gateway `:5100`, kèm token Receptionist —
tạo được bệnh nhân, list có phân trang + search, sửa, xoá mềm (xoá rồi list không còn thấy nhưng
bản ghi vẫn nằm trong DB), và token role Doctor gọi `POST /api/patients` bị 403.

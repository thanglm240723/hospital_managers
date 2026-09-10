# 00 — Quyết định & quy ước chung

File này chỉ chứa **quy ước dùng chung cho mọi module**. Việc cụ thể của từng luồng nằm ở file riêng.

> Đã đối chiếu với code thật tại commit `9496ffb`.

## A. Những quyết định đang áp dụng

Muốn khác thì sửa ở đây trước rồi mới code — các file sau đều dựa vào mục này.

| Vấn đề | Chốt | Lý do |
|---|---|---|
| Database | **SQL Server** qua EF Core `10.0.12`, code-first + Migrations | `appsettings.json` sẵn connection string LocalDB |
| Provider | `SqlServer` (chính) + `InMemory` (để dành cho test) | Đã cài xong |
| Lớp nền entity | **`Entity<TId>` + `AggregateRoot<TId>`** đã có trong `Domain/Common/` | Rich domain model: private setter + factory method |
| Kiểu thời gian | **`DateTimeOffset`**, lưu UTC | Theo code đã viết. FE đổi sang giờ VN khi hiển thị |
| Khoá chính | `Guid` sinh bằng `Guid.CreateVersion7()` | Tuần tự theo thời gian, không phân mảnh index như `NewGuid()` |
| Định danh đăng nhập | **Email** | `User` không có `UserName` |
| Phát hành + validate token | **CleanArchCqrs.API** | Gateway không có tầng auth, chỉ forward header |
| Kiểu token | JWT HS256, access token 60 phút | Refresh token để backlog |
| Phân quyền | Role-based, 1 user 1 role (`[Authorize(Roles = ...)]`) | Đủ cho nghiệp vụ bệnh viện |
| Kiến trúc service | **Monolith 1 API**, gateway đã sẵn 6 cluster | Tách sau chỉ đổi `Address` trong `appsettings.json` |
| Xoá dữ liệu | **Soft delete** (`IsDeleted`) cho hồ sơ nghiệp vụ | Hồ sơ y tế không được xoá cứng |

⚠ **`User` không có `IsDeleted`** — dùng `IsActive` để khoá tài khoản là đủ. Soft delete chỉ áp cho
`Patient`, `MedicalRecord`, `Appointment`, `Invoice`. Đừng thêm `IsDeleted` vào `User` cho "đồng bộ".

⚠ **Không dùng `BaseEntity` / `IAggregateRoot`.** Bản plan đầu có đề xuất nhưng code đã đi hướng
`Entity<TId>` generic — tốt hơn và đã chạy. Entity mới kế thừa `AggregateRoot<Guid>` nếu là aggregate root,
`Entity<Guid>` nếu không.

## B. Quy tắc kiến trúc — không được vi phạm

```
Domain          ← không tham chiếu project nào, không package nào
Application     → Domain
Infrastructure  → Domain, Application
API             → Application, Infrastructure
Gateway         → không tham chiếu project nào
```

Kiểm tra nhanh: mở `.csproj`, nếu `Domain` có bất kỳ `<ProjectReference>` hay `<PackageReference>` nào là đã sai.

**Hệ quả thực tế:**
- Domain không được `using Microsoft.EntityFrameworkCore` → không đặt `[Table]`, `[Key]`, `[MaxLength]`,
  `[NotMapped]` lên entity. Cấu hình toàn bộ bằng Fluent API bên Infrastructure.
- Domain không được `using MediatR` → `IDomainEvent` **không** kế thừa `INotification`.
  Cần wrapper `DomainEventNotification<T>` bên Application.
- Application không được `using` DbContext. Chỉ biết `IXxxRepository` khai báo ở Domain.
- Application không được `using Microsoft.AspNetCore.Http`. Cần thông tin request thì khai interface
  (`ICurrentUser`), implement bên API.
- Controller không được `using` Infrastructure. Chỉ gọi `IMediator`.

## C. Quy ước đặt tên

**C#**
- File-scoped namespace. 1 file = 1 type, tên file = tên type.
- Tổ chức theo **feature folder**, không theo kiểu kỹ thuật:
  `Domain/Identity/{User.cs, IUserRepository.cs, Events/}` — không phải `Domain/Entities/`, `Domain/Interfaces/`.
- Command/Query là `record` positional, implement `IRequest<T>`.
- Domain event là `sealed record ... : DomainEvent`, hậu tố `DomainEvent`, đặt trong `<Feature>/Events/`.
- Handler **tách file riêng**, đặt **cùng folder** với command:
  `Auth/Commands/Login/{LoginCommand.cs, LoginCommandHandler.cs}`
- Validator: `<Feature>/Validators/XxxCommandValidator.cs`
- Hằng vai trò: class số nhiều `Roles`, hằng số ít `Roles.Admin`. Property trên entity là `Role` (số ít).
- Field private: `_camelCase`. Constructor injection, không dùng property injection.
- Method bất đồng bộ: hậu tố `Async`, **luôn** nhận `CancellationToken ct = default`.
- DI theo layer: `DependencyInjection/XxxServiceExtensions.cs` → `AddXxxServices(this IServiceCollection)`.

**Đặt tên use-case** — dùng động từ nghiệp vụ, không dùng CRUD chung chung:
- ✅ `CheckInPatientCommand`, `CancelAppointmentCommand`, `IssueInvoiceCommand`
- ❌ `UpdatePatientStatusCommand`, `UpdateAppointmentCommand`

**JavaScript / React**
- Feature-based: `src/feature/<Feature>/{index.js, Container.js, api.js, component/, redux/}`
- `index.js` là barrel: `export default Container` + export reducer/action.
- Absolute import từ `src/` (đã bật `NODE_PATH=src`): `import Helper from 'lib/helper'`.
- Action type namespace hoá — đổi tiền tố `KYC/` cũ thành `HMS/`: `'HMS/AUTH/LOGIN'`.
- Const action type **export từ `reducer.js`**, `action.js` import ngược lại.
- SCSS theo ITCSS + BEM: `c-patient-list__row`, modifier `c-badge--danger`.
- Prettier: singleQuote, trailingComma `all`, tabWidth 2, printWidth **150**.

## D. Model dùng chung — phân trang

Các module có danh sách đều dùng chung 2 type này. Tạo khi làm module đầu tiên có phân trang
(module Patients), không cần cho luồng login.

**`Application/Common/Models/PagedResult.cs`**

```csharp
public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int TotalCount { get; init; }
    public int PageNumber { get; init; }
    public int PageSize { get; init; }
    public int TotalPages => PageSize == 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
}
```

**`Application/Common/Models/PagedQuery.cs`** — base record cho mọi query có phân trang:

```csharp
public abstract record PagedQuery
{
    public int PageNumber { get; init; } = 1;
    public int PageSize { get; init; } = 20;
    public string? SearchTerm { get; init; }
}
```

Kèm hàm chuẩn hoá: `PageNumber < 1 → 1`, `PageSize` kẹp trong `[1, 100]`.

⚠ Template gốc định nghĩa `PagedResult<T>` **2 lần** (Domain và Application). Chỉ giữ **1 bản ở Application**.
Repository bên Domain trả `(IReadOnlyList<T> Items, int TotalCount)` — **không** trả `PagedResult`,
vì đó là model của tầng Application.

⚠ Tên tham số phải khớp giữa BE và FE: `pageNumber`, `pageSize`, `searchTerm`.

## E. Cấu trúc URL

Gateway đã cố định 6 prefix trong `Gateway/appsettings.json`. Controller bên API **phải** khớp:

| Gateway route | Controller | `[Route]` |
|---|---|---|
| `/api/auth/**` | `AuthController` | `api/auth` |
| `/api/patients/**` | `PatientsController` | `api/patients` |
| `/api/doctors/**` | `DoctorsController` | `api/doctors` |
| `/api/appointments/**` | `AppointmentsController` | `api/appointments` |
| `/api/medical-records/**` | `MedicalRecordsController` | `api/medical-records` |
| `/api/billing/**` | `BillingController` | `api/billing` |

⚠ `[Route("api/[controller]")]` sinh ra `api/medicalrecords` (không có gạch nối) — **không khớp** route gateway.
Với `MedicalRecordsController` phải viết tay: `[Route("api/medical-records")]`.

Cả 6 cluster đều trỏ về `http://localhost:5289/` (monolith). Khi tách service chỉ đổi `Address`.

## F. Xử lý lỗi — chuẩn chung

Mọi lỗi trả về theo **RFC 7807 Problem Details**, map trong `GlobalExceptionHandlerMiddleware`:

| Exception | Status |
|---|---|
| `ValidationException` (Application) | 400, kèm `errors` |
| `UnauthorizedAccessException` | 401 |
| `NotFoundException` (Domain) | 404 |
| `BusinessRuleViolationException` (Domain) | 409 |
| còn lại | 500 — log full, response chỉ trả message chung |

⚠ Không bao giờ trả `ex.ToString()` ra response ở Production.

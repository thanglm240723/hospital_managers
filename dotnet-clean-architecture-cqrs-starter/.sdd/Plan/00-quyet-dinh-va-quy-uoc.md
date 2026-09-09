# 00 — Quyết định & quy ước

## A. Những giả định plan này đang dùng

Nếu bạn muốn khác, sửa ở đây trước rồi mới code — các file sau đều dựa vào mục này.

| Vấn đề | Plan chọn | Lý do |
|---|---|---|
| Database | **SQL Server** qua EF Core 9, code-first + Migrations | `appsettings.json` sẵn connection string LocalDB |
| Provider hiện có | Chỉ `InMemory` | ⚠ **phải cài thêm** `Microsoft.EntityFrameworkCore.SqlServer` |
| Phát hành token | **CleanArchCqrs.API** | Bạn đã chốt: BE phát hành |
| Validate token | **CleanArchCqrs.API** | Gateway không còn tầng auth |
| Kiểu token | JWT HS256, access token 60 phút | Đủ cho giai đoạn đầu; refresh token xem phase 2 mục F |
| Phân quyền | Role-based (`[Authorize(Roles = ...)]`) | Đơn giản, đủ cho nghiệp vụ bệnh viện |
| Kiến trúc service | **Monolith 1 API**, gateway đã sẵn 6 cluster | Tách sau chỉ đổi `Address` |
| Xoá dữ liệu | **Soft delete** (`IsDeleted`) | Hồ sơ y tế không được xoá cứng |
| Múi giờ | Lưu **UTC** trong DB, đổi sang giờ VN ở FE | Tránh lệch giờ khi deploy |

## B. Package cần cài thêm

Chưa có trong `.csproj`, phải thêm trước khi code:

```bash
cd D:/hospital_management/dotnet-clean-architecture-cqrs-starter

# Infrastructure — SQL Server + tooling migration
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.EntityFrameworkCore.SqlServer
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.EntityFrameworkCore.Design

# API — validate JWT
dotnet add src/CleanArchCqrs.API package Microsoft.AspNetCore.Authentication.JwtBearer

# Infrastructure — phát hành JWT + hash password
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.IdentityModel.JsonWebTokens
dotnet add src/CleanArchCqrs.Infrastructure package Microsoft.Extensions.Identity.Core

# CLI migration (1 lần cho máy)
dotnet tool install --global dotnet-ef
```

⚠ EF Core hiện đang pin `9.0.4`. Giữ **cùng version** cho mọi package `Microsoft.EntityFrameworkCore.*`,
lệch version sẽ lỗi lúc chạy migration.

## C. Quy tắc kiến trúc — không được vi phạm

```
Domain          ← không tham chiếu project nào, không package nào
Application     → Domain
Infrastructure  → Domain, Application
API             → Application, Infrastructure
Gateway         → không tham chiếu project nào
```

Kiểm tra nhanh: mở `.csproj`, nếu `Domain` có `<ProjectReference>` hoặc `<PackageReference>` là đã sai.

**Hệ quả thực tế:**
- Domain không được `using Microsoft.EntityFrameworkCore` → không đặt `[Table]`, `[Key]` lên entity. Cấu hình bằng Fluent API bên Infrastructure.
- Application không được `using` DbContext. Chỉ biết `IXxxRepository` khai báo ở Domain.
- Controller không được `using` Infrastructure. Chỉ gọi `IMediator`.

## D. Quy ước đặt tên (giữ đúng của codebase gốc)

**C#**
- File-scoped namespace: `namespace CleanArchCqrs.Domain.Entities;`
- 1 file = 1 type. Tên file = tên type.
- XML doc `///` trên mọi public type và public method.
- Command/Query là `record` positional, implement `IRequest<T>`.
- Handler **tách file riêng**, đặt **cùng folder** với command:
  `Patients/Commands/CreatePatient/{CreatePatientCommand.cs, CreatePatientCommandHandler.cs}`
- Validator: `Patients/Validators/CreatePatientCommandValidator.cs`
- Field private: `_camelCase`. Constructor injection, không dùng property injection.
- Method bất đồng bộ: hậu tố `Async`, **luôn** nhận `CancellationToken cancellationToken = default`.
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

## E. Cấu trúc URL

Gateway đã cố định 6 prefix. Controller bên API **phải** khớp:

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

✓ **Xong phase 0 khi:** đã chạy hết lệnh mục B, `dotnet build` vẫn 0 error.

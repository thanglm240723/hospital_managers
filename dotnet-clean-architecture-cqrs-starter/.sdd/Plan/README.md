# Plan triển khai Hospital Management

Kế hoạch code tay từng bước cho 3 codebase:

> Plan này đặt tại `<repo-be>/.sdd/Plan/`. Mọi đường dẫn trong plan tính từ **`<workspace>` = `D:/hospital_management`**
> (thư mục cha chứa cả 2 repo), trừ khi ghi rõ khác.

| Codebase | Đường dẫn | Trạng thái hiện tại |
|---|---|---|
| Backend | `<workspace>/dotnet-clean-architecture-cqrs-starter/` | 4 project rỗng (chỉ còn `.csproj`, `Program.cs`, 2 `ServiceExtensions`) |
| Gateway | `<workspace>/dotnet-clean-architecture-cqrs-starter/src/CleanArchCqrs.Gateway/` | **Đã xong** — YARP routing thuần, 6 route |
| Frontend | `<workspace>/react-codebase/` | Chỉ còn `src/index.js` (đang lỗi import), `src/scss/`, `config/`, `scripts/` |

## Thứ tự thực hiện

Làm tuần tự. Mỗi phase phụ thuộc phase trước.

| # | File | Nội dung | Ước lượng |
|---|---|---|---|
| 0 | [00-quyet-dinh-va-quy-uoc.md](00-quyet-dinh-va-quy-uoc.md) | Chốt DB, package cần thêm, quy ước đặt tên | 30 phút |
| 1 | [01-be-nen-tang.md](01-be-nen-tang.md) | Base entity, exception, pipeline behavior, DbContext, error middleware | 1 ngày |
| 2 | [02-be-auth.md](02-be-auth.md) | User, hash password, **phát hành JWT**, validate JWT, `/api/auth` | 1–2 ngày |
| 3 | [03-be-module-patients.md](03-be-module-patients.md) | Vertical slice mẫu đầy đủ — **học thuộc file này** | 1–2 ngày |
| 4 | [04-be-cac-module-con-lai.md](04-be-cac-module-con-lai.md) | Doctors, Appointments, MedicalRecords, Billing | 4–6 ngày |
| 5 | [05-gateway.md](05-gateway.md) | Việc cần làm khi tách service | 2 giờ |
| 6 | [06-fe-nen-tang.md](06-fe-nen-tang.md) | Sửa `index.js`, store, router, http client, layout | 1 ngày |
| 7 | [07-fe-auth.md](07-fe-auth.md) | Màn login, lưu token, route guard | 1 ngày |
| 8 | [08-fe-nghiep-vu.md](08-fe-nghiep-vu.md) | Các màn CRUD | 4–6 ngày |

## Quy ước đọc plan

- **`→ Tạo`** — file cần tạo mới.
- **`→ Sửa`** — file đã tồn tại, cần chỉnh.
- **`✓ Xong khi`** — tiêu chí nghiệm thu, tự kiểm tra được.
- **`⚠`** — chỗ dễ sai, đọc kỹ.
- Code trong plan là **khung ký hiệu** (signature, shape), không phải bản hoàn chỉnh để copy — phần thân bạn tự viết.

## Sơ đồ tổng thể

```
Browser (React :9000)
        │  Authorization: Bearer <token>
        ▼
Gateway YARP (:5100)          ← chỉ định tuyến, forward header nguyên vẹn
        │
        ▼
CleanArchCqrs.API (:5289)     ← phát hành + validate JWT
        │
   Application (CQRS/MediatR)
        │
      Domain  ←  Infrastructure (EF Core)
```

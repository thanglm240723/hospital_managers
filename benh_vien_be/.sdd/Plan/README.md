# Plan triển khai Hospital Management

Kế hoạch code tay từng bước cho 3 codebase:

> Plan này đặt tại `<repo-be>/.sdd/Plan/`. Mọi đường dẫn trong plan tính từ **`<workspace>` = `D:/hospital_management`**
> (thư mục cha chứa cả 2 repo), trừ khi ghi rõ khác.

| Codebase | Đường dẫn | Trạng thái hiện tại |
|---|---|---|
| Backend | `<workspace>/benh_vien_be/` | **Domain đã xong** (`Entity`, `AggregateRoot`, `User`, 5 domain event). Application / Infrastructure / API vẫn rỗng |
| Gateway | `<workspace>/benh_vien_be/src/CleanArchCqrs.Gateway/` | **Đã xong** — YARP routing thuần, 6 route |
| Frontend | `<workspace>/benh_vien_fe/` | Chỉ còn `src/index.js` (đang lỗi import), `src/scss/`, `config/`, `scripts/` |

## Thứ tự thực hiện

Làm tuần tự. Mỗi phase phụ thuộc phase trước.

| # | File | Nội dung | Ước lượng |
|---|---|---|---|
| 0 | [00-quyet-dinh-va-quy-uoc.md](00-quyet-dinh-va-quy-uoc.md) | Quy ước chung: kiến trúc, đặt tên, URL, xử lý lỗi, phân trang | đọc 15 phút |
| 1–2 | [luong-login.md](luong-login.md) | **Luồng login đầy đủ** — sửa Domain, DbContext, pipeline, JWT, `/api/auth`, seed admin | 2–3 ngày |
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

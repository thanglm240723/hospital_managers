# 08 — Frontend: các màn nghiệp vụ

Làm theo thứ tự backend: **Patients → Doctors → Appointments → MedicalRecords → Billing**.

## Khuôn mẫu một feature CRUD

Làm kỹ `Patients` trước, 4 feature sau chép công thức.

```
src/feature/Patients/
├── index.js                        barrel
├── Container.js                    màn danh sách (list + search + phân trang)
├── api.js                          getPatients / getPatient / createPatient / updatePatient / deletePatient
├── component/
│   ├── PatientTable/index.js       bảng, nhận data qua props — KHÔNG tự gọi API
│   ├── PatientForm/
│   │   ├── index.js                redux-form, dùng chung cho cả tạo và sửa
│   │   └── validation.js
│   └── PatientDetail/index.js      xem chi tiết
└── redux/
    ├── action.js
    └── reducer.js
```

⚠ **Chia đôi vai trò rõ ràng** (quy ước sẵn có của codebase):
- `Container.js` — biết Redux, biết API, giữ state, xử lý sự kiện.
- `component/**` — thuần trình bày, chỉ nhận `props`, không `connect`, không gọi API.

Sai chỗ này là feature sau không tái dùng được component nào.

## Bước 8.1 — Patients

**`api.js`**

```js
export const getPatients = async params => {
  const { data } = await http.get({ path: urlApi.patients.base, params });
  return data;                      // PagedResult { items, totalCount, pageNumber, ... }
};
```

⚠ Tên tham số phải khớp `PagedQuery` bên BE: `pageNumber`, `pageSize`, `searchTerm`.
Sai tên thì BE lặng lẽ dùng giá trị mặc định, không báo lỗi — rất khó phát hiện.

**`Container.js`** giữ state: `items`, `totalCount`, `pageNumber`, `pageSize`, `searchTerm`, `selected`.

⚠ **Search phải debounce ~400ms.** Gõ mỗi ký tự bắn 1 request là cách nhanh nhất làm sập API.

**`PatientForm`** dùng chung cho tạo và sửa — phân biệt bằng prop `initialValues`:
có thì là sửa (gọi PUT), không có thì là tạo (gọi POST).

⚠ Trường **`Allergies` (dị ứng)** phải hiển thị nổi bật ở màn chi tiết — badge đỏ, không nhét
xuống cuối form. Đây là thông tin an toàn tính mạng.

⚠ Ngày sinh: BE trả `DateOnly` dạng chuỗi `"1990-05-20"`. **Không** đưa qua `new Date()` rồi format,
sẽ lệch 1 ngày do timezone. Tách chuỗi thủ công hoặc parse như local date.

## Bước 8.2 — Doctors

Giống Patients. Thêm màn chọn bác sĩ theo chuyên khoa (dùng cho đặt lịch).
Chỉ role `Admin` thấy menu này.

## Bước 8.3 — Appointments

Feature phức tạp nhất bên FE.

**Ba khung nhìn:**
| Màn | Dùng cho |
|---|---|
| Lịch hẹn hôm nay | Receptionist — check-in |
| Lịch của tôi | Doctor — bắt đầu khám |
| Đặt lịch mới | Receptionist |

**Luồng đặt lịch:** chọn bệnh nhân → chọn chuyên khoa → chọn bác sĩ → chọn ngày →
hiện khung giờ trống → xác nhận.

⚠ Danh sách khung giờ trống **phải lấy từ BE**, đừng tự tính ở FE. FE không biết lịch của
bác sĩ với bệnh nhân khác.

**Nút hành động đổi theo `status`** — đây là chỗ dễ sai nhất:

| Status | Nút hiện ra | Role |
|---|---|---|
| `Scheduled` | Check-in, Đổi lịch, Huỷ | Receptionist |
| `CheckedIn` | Bắt đầu khám | Doctor |
| `InProgress` | Hoàn thành + ghi hồ sơ | Doctor |
| `Completed` | Xem hồ sơ, Xuất hoá đơn | Doctor / Accountant |
| `Cancelled` / `NoShow` | (không có) | |

⚠ Mỗi nút gọi **endpoint riêng** (`/check-in`, `/start`, `/complete`), không phải một hàm
`updateStatus` chung — khớp với các use-case theo động từ bên BE
(xem [04-be-cac-module-con-lai.md](04-be-cac-module-con-lai.md) mục 4.2).

Dùng bảng ánh xạ `status → { label, color, actions }` đặt trong `feature/Appointments/constants.js`,
đừng viết chuỗi `if/else` trong JSX.

## Bước 8.4 — MedicalRecords

⚠ Path là **`medical-records`** (có gạch nối), khớp route gateway.

Màn ghi hồ sơ mở từ lịch hẹn đang `InProgress`, không cho tạo rời rạc — buộc gắn với ca khám.

Form gồm: lý do khám, triệu chứng, chẩn đoán, mã ICD, hướng điều trị, **đơn thuốc (danh sách động)**.

⚠ Đơn thuốc là mảng — dùng `FieldArray` của redux-form, thêm/xoá dòng được.

⚠ Hồ sơ đã `IsFinalized` → form chuyển **read-only toàn bộ**, ẩn nút Lưu.
Hiện badge "Đã ký" + thời điểm ký. Muốn sửa thì tạo bản đính chính.

Receptionist và Accountant **không được vào feature này** — chặn cả ở `PrivateRoute` lẫn sidebar.

## Bước 8.5 — Billing

Màn tạo hoá đơn: chọn bệnh nhân → chọn ca khám → thêm các dòng dịch vụ → nhập BHYT chi trả → phát hành.

⚠ **`TotalAmount` hiển thị là do FE tính để xem trước.** Số tiền thật do BE tính lại khi lưu.
Không gửi `totalAmount` lên BE.

⚠ Tiền tệ format kiểu VN: `1.500.000 ₫`. Dùng `Intl.NumberFormat('vi-VN')`, đừng tự viết hàm chèn dấu chấm.

⚠ Hoá đơn đã `Issued` → không cho sửa dòng, chỉ còn nút "Ghi nhận thanh toán" và "Huỷ hoá đơn".

## Checklist chung mỗi feature FE

- [ ] Đủ bộ file: `index.js`, `Container.js`, `api.js`, `component/`, `redux/`
- [ ] `Container` giữ state + gọi API; `component/` thuần props
- [ ] Absolute import (`import { http } from 'service'`), không `../../..`
- [ ] Action type tiền tố `HMS/<FEATURE>/<ACTION>`
- [ ] Mọi lệnh gọi API bọc trong `loadingAction`
- [ ] Lỗi API đọc từ Problem Details, hiện `c-text--error`, không `alert()`
- [ ] Class CSS theo BEM, tái dùng `src/scss/components/` sẵn có
- [ ] Route khai báo trong `Routes.js` kèm `roles` khớp BE
- [ ] Search có debounce
- [ ] Đã thử với ít nhất 2 role khác nhau

✓ **Xong phase 8 khi:** chạy trọn kịch bản bằng UI, đăng nhập đúng role ở mỗi bước —
Receptionist tiếp nhận bệnh nhân và đặt lịch → check-in → Doctor khám và ghi hồ sơ →
Accountant xuất hoá đơn và ghi nhận thanh toán.

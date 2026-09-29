---
name: react-feature
description: Lập kế hoạch hoặc triển khai màn hình/feature mới trong benh_vien_fe của HMS theo feature folder, Redux (HMS/ action type), service/http.js, route guard, quyền <Can>, xử lý Problem Details và Jest.
argument-hint: "<màn hình / feature>"
---
Với `$ARGUMENTS`:

1. Xác định API backend dùng tới (route `/api/v1/...`, DTO, mã lỗi, quyền) — nếu API chưa có hoặc cần đổi, dừng và dùng
   skill `api-contract-change`/`feature` cho phần backend trước.
2. Tạo `src/feature/<Feature>/`:
   - `api/` — hàm gọi qua `service/http.js` (không axios trực tiếp);
   - `redux/` — `reducer.js` (export const action type `'HMS/<FEATURE>/<ACTION>'`), `action.js`, epic/thunk như feature Auth;
   - `component/` — component trình bày; `Container.js` — connect store; `index.js` — barrel;
   - `__tests__/` — Jest cho reducer, api, component quan trọng.
3. Đăng ký reducer ở `src/reducer.js`, route ở router (dưới `PrivateRoute` nếu cần đăng nhập), ẩn/hiện bằng `<Can permission="…">`.
4. Lỗi: đọc `code`/`errors` từ Problem Details (tham khảo `feature/Auth/problem.js`); hiển thị lỗi validation theo trường;
   412 ⇒ báo nạp lại; không tự retry command không có `Idempotency-Key`.
5. Dữ liệu nhạy cảm: không lưu vào storage trình duyệt, xóa khỏi state khi rời màn/đăng xuất; che số định danh theo quyền nếu API trả dạng che.
6. Trạng thái chuyên môn/tài chính/kết quả/tệp hiển thị riêng từng cái; giờ hiển thị theo `Asia/Ho_Chi_Minh`.
7. SCSS: `src/scss/components/_component.<ten>.scss` theo BEM, import trong `scss/index.scss`.
8. Kiểm tra: `tooling/validate.ps1 -Mode Frontend`.

Mặc định người dùng tự viết code: chưa được yêu cầu implement thì trả về kế hoạch (file, props/state, action, API, test).

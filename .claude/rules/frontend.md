---
paths:
  - "react-codebase/src/**/*"
---
# Quy tắc frontend (react-codebase)

## Cấu trúc
- `src/feature/<Feature>/{index.js, Container.js, api/, component/, redux/, __tests__/}`; `index.js` là barrel
  (`export default Container` + reducer/action). Feature đăng ký vào `src/feature/index.js` và `src/reducer.js`.
- Import tuyệt đối từ `src/` (`import http from 'service/http'`).
- Redux: action type `'HMS/<FEATURE>/<ACTION>'`, const action type export từ `reducer.js`, `action.js` import ngược lại.
  Side effect bằng redux-observable/thunk như code hiện có.
- SCSS: ITCSS (`settings/tools/generics/elements/objects/components/utilities`) + BEM (`c-patient-list__row`, `c-badge--danger`).
- Prettier: singleQuote, trailingComma `all`, tabWidth 2, printWidth 150. ESLint phải sạch.

## HTTP và phiên
- Mọi request qua `service/http.js`: Bearer từ `feature/Auth/session/tokenStore`, header CSRF, refresh một lần khi 401
  (single-flight + điều phối đa tab trong `refreshCoordinator`). Không gọi `axios` trực tiếp trong feature.
- URL luôn qua Gateway, dạng `/api/v1/...`; không gọi thẳng API `:5289`.
- Access token chỉ trong RAM. **Không** lưu token, dữ liệu bệnh án hay PHI vào `localStorage`/`sessionStorage`/IndexedDB.
  Đăng xuất xóa state.
- Lỗi đọc từ Problem Details (`code`, `title`, `errors`) — xem `feature/Auth/problem.js`. `412` ⇒ báo dữ liệu đã đổi,
  yêu cầu nạp lại, không tự merge. `409 operation_in_progress` ⇒ chờ theo `Retry-After`.
- Không tự retry command không có `Idempotency-Key`; command có idempotency gửi lại cùng key khi retry.

## Quyền và hiển thị
- Quyền từ `GET /api/v1/auth/me` (lưu ở `state.auth`); ẩn/hiện bằng `<Can permission="users.read">` hoặc
  `hasPermission(state.auth, code)` trong `feature/Auth/permissions.js`. Mã quyền phải khớp `Domain/Identity/Permissions.cs`.
  Đây chỉ là UX — backend mới là nơi quyết định.
- Route cần đăng nhập đặt dưới `PrivateRoute`.
- Trạng thái chuyên môn, tài chính, kết quả, tệp hiển thị riêng từng cái, không gộp.
- Không `dangerouslySetInnerHTML` với dữ liệu chưa sanitize. Giờ hiển thị đổi sang `Asia/Ho_Chi_Minh` ở UI; dữ liệu gửi đi là UTC.

## Test
- Jest, file trong `__tests__/` cạnh code. Chạy: `tooling/validate.ps1 -Mode Frontend`.

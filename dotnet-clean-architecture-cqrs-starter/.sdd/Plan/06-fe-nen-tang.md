# 06 — Frontend: nền tảng

Hiện `<workspace>/react-codebase/src/` chỉ còn `index.js`, `serviceWorker.js`, `scss/`.
**`src/index.js` đang lỗi** — còn 2 import trỏ vào thư mục đã xoá.

## Bước 6.1 — Chữa `src/index.js`

**→ Sửa `src/index.js`**

Hai dòng lỗi:
```js
import { App, Login, Register, ForgotPassword, ResetPassword } from 'feature';  // dòng 14 — 'feature' không còn
import combinedReducers from './reducer';                                        // dòng 15 — file đã xoá
```

Phần **giữ nguyên** (đang đúng, đừng viết lại): tạo `history`, `routerMiddleware`,
`composeEnhancers` bọc Redux DevTools, `connectRouter(history)(reducers)`, `<BrowserRouter>` +
`<Provider>` + `<MuiThemeProvider>`, `serviceWorker.unregister()`.

Phần **sửa**: trỏ import sang các feature mới, và `<Switch>` khai báo route theo bước 6.5.

⚠ `applyMiddleware` đang bị gọi **3 lần liên tiếp** trong file gốc, trong đó `routerMiddleware(history)`
bị đăng ký 2 lần. Gộp lại thành một: `applyMiddleware(middleware, thunk)`.

## Bước 6.2 — Dựng lại `src/reducer.js`

**→ Tạo `src/reducer.js`**

```js
import { combineReducers } from 'redux';
import { reducer as form } from 'redux-form';

// IMPORT_STORES
import { loadingReducer } from 'feature/Loading';
import { authReducer } from 'feature/Auth';

export default combineReducers({
  // CONNECT_STORES
  auth: authReducer,
  form,
  loadingModal: loadingReducer,
});
```

⚠ File gốc `import { routerReducer } from 'react-router-redux'` — **package đó không có trong
`package.json`**, đây là lỗi có sẵn của codebase. Đừng chép lại.
`connectRouter(history)` trong `index.js` đã lo phần router state rồi, `reducer.js` không cần đụng tới.

⚠ Giữ 2 comment `// IMPORT_STORES` và `// CONNECT_STORES` — quy ước của codebase để biết chỗ chèn.

## Bước 6.3 — Biến môi trường

**→ Sửa `.env`**

```
PORT=9000
NODE_PATH=src/
API_URL=http://localhost:5100/api
TOKEN_STORAGE=hms_token
USER_STORAGE=hms_user
```

⚠ **`API_URL` trỏ vào gateway `:5100`, không phải API `:5289`.**

⚠ **Bắt buộc sửa `config/env.js`.** Ejected config hardcode whitelist biến env ở dòng 80–83:

```js
KYC_API: process.env.KYC_API,      // → đổi thành API_URL
USER_STORAGE: process.env.USER_STORAGE,
PORT: process.env.PORT,
COOKIE_NAME: process.env.COOKIE_NAME,   // → đổi thành TOKEN_STORAGE
```

Biến không nằm trong danh sách này (và không có tiền tố `REACT_APP_`) sẽ là `undefined` lúc chạy —
lỗi rất khó lần vì không báo gì cả.

**→ Sửa `pm2.json`** — cập nhật `env` tương ứng, đổi `name` từ `kyc-client` thành `hms-client`.

## Bước 6.4 — Proxy dev để tránh CORS

**→ Sửa `config/webpackDevServer.config.js`**

Thêm vào object config trả về:

```js
proxy: {
  '/api': { target: 'http://localhost:5100', changeOrigin: true },
},
```

Rồi để `API_URL=/api` (đường dẫn tương đối) khi chạy dev — browser gọi cùng origin `:9000`, hết CORS.

⚠ Cách này **chỉ có tác dụng lúc dev**. Production phải bật CORS ở gateway —
xem [05-gateway.md](05-gateway.md) mục cuối.

## Bước 6.5 — Tầng service HTTP

**→ Tạo `src/service/http.js`**

Thay cho `kycHttp.js` cũ. Giữ nguyên hình dạng (object singleton bọc axios, có `get/post/put/delete`)
nhưng đổi 3 điểm:

| Cũ | Mới |
|---|---|
| header `x-kyc-auth` | **`Authorization: Bearer <token>`** |
| token lưu trong biến của module | đọc từ `localStorage` mỗi request |
| `withCredentials: true` | bỏ — dùng token, không dùng cookie |

Thêm **interceptor response**: gặp 401 → xoá token, redirect `/login`.
Đây là chỗ duy nhất xử lý hết hạn token, đừng rải ra từng màn hình.

⚠ Backend trả lỗi theo chuẩn **RFC 7807 Problem Details**. Viết sẵn hàm bóc lỗi:
`title` là message chung, `errors` (nếu có) là lỗi từng field để đổ vào `redux-form`.

**→ Tạo `src/service/urlApi.js`** — gom toàn bộ path, giữ đúng quy ước cũ:

```js
export default {
  auth: { login: 'auth/login', me: 'auth/me', changePassword: 'auth/change-password' },
  patients: { base: 'patients', byId: id => `patients/${id}` },
  doctors: { base: 'doctors', available: 'doctors/available' },
  appointments: { base: 'appointments', checkIn: id => `appointments/${id}/check-in` },
  medicalRecords: { base: 'medical-records' },
  billing: { base: 'billing' },
};
```

**→ Tạo `src/service/index.js`** — barrel: `export { http, urlApi }`

## Bước 6.6 — Lib dùng chung

**→ Tạo `src/lib/helper.js`**
Giữ `setStorage` / `getStorage` / `removeStorage`. **Bỏ toàn bộ hàm cookie** — không dùng cookie nữa.
Thêm `formatDate`, `formatCurrency` (VND), `calculateAge(dateOfBirth)`.

**→ Tạo `src/lib/validation.js`** — validator cho `redux-form`: `required`, `email`, `phoneVN`,
`minLength`, `identityNumber`.

## Bước 6.7 — Component dùng chung

**→ Tạo** trong `src/component/`:

| Component | Ghi chú |
|---|---|
| `FormInput/` | wrap `Field` của redux-form + Material-UI, hiện lỗi validate |
| `Sidebar/` | menu trái, lọc item theo role của user hiện tại |
| `Modal/` | `ModalSuccessWrapper`, `ModalConfirm` |
| `DataTable/` | bảng + phân trang + ô search, dùng lại cho mọi màn danh sách |
| `PageHeader/` | tiêu đề + nút hành động |

**→ Tạo `src/component/index.js`** — barrel export tất cả.

⚠ SCSS đã có sẵn trong `src/scss/` (ITCSS + BEM) — class `c-card`, `c-panel`, `c-sidebar`,
`c-heading`, `c-row`, `c-text--error`... **Đọc `src/scss/components/` trước khi viết CSS mới.**
Component mới thì thêm file `_component.<ten>.scss` và import vào `src/scss/index.scss`.

## Bước 6.8 — Feature Loading

**→ Tạo `src/feature/Loading/`** — bộ 4 file: `index.js`, `Container.js`, `redux/action.js`, `redux/reducer.js`

`loadingAction` bọc một promise: bật spinner → chạy → tắt spinner (kể cả khi lỗi).
Dùng ở mọi nơi gọi API:

```js
await loadingAction(() => login(payload));
```

Action type đổi tiền tố: `'HMS/LOADING/SHOW'` (cũ là `KYC/`).

✓ **Xong phase 6 khi:** `npm start` chạy được ở `:9000`, không lỗi console,
và từ devtools gọi thử `http.get({ path: 'auth/me' })` thấy request đi qua gateway `:5100`.

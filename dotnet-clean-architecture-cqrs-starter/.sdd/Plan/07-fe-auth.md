# 07 — Frontend: đăng nhập & phân quyền

## Bước 7.1 — Feature Auth

**→ Tạo `src/feature/Auth/`** theo đúng bộ file chuẩn của codebase:

```
Auth/
├── index.js                      barrel: export default Container + authReducer + action
├── Container.js                  class component, connect + withRouter
├── api.js                        login / getMe / changePassword
├── component/
│   └── LoginForm/
│       ├── index.js              redux-form Field
│       └── validation.js
└── redux/
    ├── action.js
    └── reducer.js
```

**`redux/reducer.js`** — giữ quy ước cũ: export const action type **từ reducer**, action.js import ngược lại.

```js
export const AUTH_LOGIN = 'HMS/AUTH/LOGIN';
export const AUTH_LOGOUT = 'HMS/AUTH/LOGOUT';

const initialState = { user: undefined, isAuthenticated: false };
```

**`api.js`**

```js
import { http, urlApi } from 'service';

export const login = async payload => {
  const { data } = await http.post({ path: urlApi.auth.login, payload });
  return data;                      // { token, expiresAtUtc, user }
};
```

**`Container.js` — luồng `handleSubmit`:**
1. `await loadingAction(() => login({ userName, password }))`
2. Lưu token: `Helper.setStorage(process.env.TOKEN_STORAGE, token)`
3. Lưu user: `Helper.setStorage(process.env.USER_STORAGE, JSON.stringify(user))`
4. `dispatch(storeAuthLogin(user))`
5. `history.push('/')`
6. `catch` → set `state.msgError` từ `title` của Problem Details

⚠ **Không lưu token vào Redux là nguồn duy nhất.** Redux mất khi F5. `localStorage` là nguồn thật,
Redux chỉ là bản sao để render.

⚠ Codebase cũ dùng `componentWillMount` — **đã deprecated ở React 16.9**. Dùng `componentDidMount`.

## Bước 7.2 — Khôi phục phiên khi F5

**→ Tạo `src/feature/App/Container.js`**

`componentDidMount`:
1. Đọc token từ `localStorage`. Không có → `history.push('/login')`.
2. Có → gọi `getMe()` xác minh token còn sống.
3. Thành công → `storeAuthLogin(user)`, render layout.
4. Thất bại (401) → xoá storage → `/login`.

⚠ Phải chờ bước 2 xong mới render children, nếu không màn con sẽ gọi API khi chưa có user.
Dùng cờ `state.isBootstrapping`, đang bootstrap thì render `<Loading />`.

## Bước 7.3 — Route guard

**→ Tạo `src/component/PrivateRoute/index.js`**

```js
// Không có token           → <Redirect to="/login" />
// Có token, sai role       → <Redirect to="/403" />
// Hợp lệ                   → <Route component={...} />
```

Nhận prop `roles` (mảng). Không truyền `roles` = chỉ cần đăng nhập.

⚠ **Guard ở FE chỉ để ẩn UI, không phải bảo mật.** Ai cũng sửa được localStorage.
Quyền thật nằm ở `[Authorize(Roles = ...)]` bên BE. Đừng bỏ check BE vì "FE đã chặn rồi".

## Bước 7.4 — Bảng route

**→ Tạo `src/feature/App/Routes.js`** — giữ đúng shape mảng của codebase cũ, thêm field `roles`:

```js
export const RoutesApp = [
  { path: '/patients',        component: PatientList,   icon: PeopleIcon,     text: 'Bệnh nhân',
    roles: ['Admin', 'Doctor', 'Nurse', 'Receptionist'] },
  { path: '/appointments',    component: AppointmentList, icon: EventIcon,    text: 'Lịch khám',
    roles: ['Admin', 'Doctor', 'Nurse', 'Receptionist'] },
  { path: '/medical-records', component: RecordList,    icon: AssignmentIcon, text: 'Hồ sơ bệnh án',
    roles: ['Admin', 'Doctor', 'Nurse'] },
  { path: '/billing',         component: InvoiceList,   icon: ReceiptIcon,    text: 'Hoá đơn',
    roles: ['Admin', 'Accountant', 'Receptionist'] },
  { path: '/doctors',         component: DoctorList,    icon: LocalHospitalIcon, text: 'Bác sĩ',
    roles: ['Admin'] },
];
```

`Sidebar` lọc mảng này theo role của user → menu tự đúng theo quyền, không phải if/else từng chỗ.

⚠ Bảng `roles` ở FE phải **khớp** với `[Authorize(Roles=...)]` ở BE. Lệch nhau thì user thấy menu
nhưng bấm vào bị 403 — trải nghiệm rất tệ. Sửa BE thì sửa luôn file này.

## Bước 7.5 — Đăng xuất

Trong `Sidebar`: xoá `TOKEN_STORAGE` + `USER_STORAGE`, `dispatch(storeAuthLogout())`,
`history.push('/login')`.

⚠ Token JWT **không thu hồi được ở server**. Đăng xuất chỉ là xoá phía client;
token cũ vẫn hợp lệ tới lúc `exp`. Muốn thu hồi thật phải làm blacklist hoặc refresh token —
xem [02-be-auth.md](02-be-auth.md) mục F.

✓ **Xong phase 7 khi:** login thật với tài khoản seed → vào được layout;
F5 vẫn giữ đăng nhập; sửa token trong localStorage thành rác → tự đá về `/login`;
đăng nhập role Accountant → sidebar không hiện "Hồ sơ bệnh án".

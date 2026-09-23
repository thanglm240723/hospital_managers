# Giai đoạn 6 — Frontend: token trong RAM, refresh chủ động, đa tab, Redux auth

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** React giữ access token chỉ trong RAM, refresh bằng cookie `__Host-rt` (chủ động trước hạn 60s + dự phòng khi gặp 401, gửi lại request đúng 1 lần), điều phối nhiều tab (Web Locks + BroadcastChannel), Redux lưu user/permissions lấy từ `/auth/me`, màn Login / Đổi mật khẩu, guard route và `<Can>` ẩn/hiện UI.

**Spec:** [`spec.md`](spec.md) §5, §3.1 · **Ràng buộc chung:** [`plan.md`](plan.md#global-constraints) · **Cần xong:** Giai đoạn 5

Mọi lệnh chạy tại `react-codebase/`.

## Điều kiện trước (nền tảng FE)

Làm theo `.sdd/Plan/06-fe-nen-tang.md` **bước 6.1, 6.2, 6.3, 6.4, 6.8**, với các sửa đổi sau (plan cũ viết trước quyết định token-trong-RAM):

- **Bước 6.3** — `.env` chỉ cần `PORT=9000`, `NODE_PATH=src/`, `API_URL=/api`. **Không** tạo `TOKEN_STORAGE`. Whitelist trong `config/env.js`: đổi `KYC_API` → `API_URL`, **xoá** dòng `COOKIE_NAME`.
- **Bước 6.4** — proxy dev `/api` → `http://localhost:5100` (Gateway) là **bắt buộc**: cookie `__Host-rt` là host-only, FE và API phải cùng origin (`http://localhost:9000`). Chrome/Firefox coi `http://localhost` là ngữ cảnh an toàn nên vẫn nhận cookie `Secure`.
- **Bỏ bước 6.5** (`service/http.js` bản localStorage) — Task 6.3 dưới đây thay thế.
- `npm install` chạy được (Node phù hợp với `node-sass@4` — Node 10/12; nếu máy dùng Node mới, cài qua `nvm`).

Chạy test FE: `CI=true npm test -- <đường-dẫn>` (bash) — `CI=true` để Jest không vào chế độ watch.

⚠ Mọi import trong code/test dưới đây dùng **đường dẫn tương đối** (Jest của bản eject này không đọc `NODE_PATH`).

---

### Task 6.1: Kho token trong RAM và đọc cookie CSRF

**Files:**
- Create: `src/feature/Auth/session/tokenStore.js`
- Create: `src/feature/Auth/session/csrf.js`
- Test: `src/feature/Auth/session/__tests__/tokenStore.test.js`, `src/feature/Auth/session/__tests__/csrf.test.js`

**Interfaces:**
- Produces:
  - `tokenStore`: `getAccessToken(): string|null`, `getExpiresAt(): number|null` (epoch ms), `setAccessToken(accessToken, expiresAtUtc)`, `clearAccessToken()`, `subscribe(listener) → unsubscribe` (listener nhận `{accessToken, expiresAt}` hoặc `null`).
  - `csrf`: `CSRF_COOKIE = '__Host-csrf'`, `readCsrfToken(cookieString = document.cookie): string|null`.

- [ ] **Step 1: Viết test (đỏ)**

`src/feature/Auth/session/__tests__/tokenStore.test.js`:

```js
import {
  getAccessToken, getExpiresAt, setAccessToken, clearAccessToken, subscribe,
} from '../tokenStore';

afterEach(() => clearAccessToken());

it('keeps the access token and its expiry in memory', () => {
  setAccessToken('abc', '2026-09-23T08:15:00Z');

  expect(getAccessToken()).toBe('abc');
  expect(getExpiresAt()).toBe(Date.parse('2026-09-23T08:15:00Z'));
});

it('notifies subscribers on set and clear, and stops after unsubscribe', () => {
  const listener = jest.fn();
  const unsubscribe = subscribe(listener);

  setAccessToken('abc', '2026-09-23T08:15:00Z');
  clearAccessToken();
  unsubscribe();
  setAccessToken('later', '2026-09-23T08:30:00Z');

  expect(listener).toHaveBeenCalledTimes(2);
  expect(listener).toHaveBeenLastCalledWith(null);
});

it('never writes to web storage', () => {
  const spy = jest.spyOn(Storage.prototype, 'setItem');

  setAccessToken('abc', '2026-09-23T08:15:00Z');

  expect(spy).not.toHaveBeenCalled();
  spy.mockRestore();
});
```

`src/feature/Auth/session/__tests__/csrf.test.js`:

```js
import { readCsrfToken } from '../csrf';

it('reads the __Host-csrf cookie among others', () => {
  expect(readCsrfToken('a=1; __Host-csrf=tok_EN-1; b=2')).toBe('tok_EN-1');
});

it('returns null when the cookie is missing', () => {
  expect(readCsrfToken('a=1')).toBeNull();
  expect(readCsrfToken('')).toBeNull();
});
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `CI=true npm test -- src/feature/Auth/session`
Expected: FAIL — `Cannot find module '../tokenStore'`.

- [ ] **Step 3: Viết code**

`src/feature/Auth/session/tokenStore.js`:

```js
// Access token chỉ sống trong RAM của tab (Đặc tả kỹ thuật §4.1):
// không Redux-persist, không localStorage/sessionStorage. F5 thì lấy lại bằng refresh cookie.
let current = null;
const listeners = new Set();

export function getAccessToken() {
  return current ? current.accessToken : null;
}

export function getExpiresAt() {
  return current ? current.expiresAt : null;
}

export function setAccessToken(accessToken, expiresAtUtc) {
  current = { accessToken, expiresAt: new Date(expiresAtUtc).getTime() };
  listeners.forEach(listener => listener(current));
}

export function clearAccessToken() {
  current = null;
  listeners.forEach(listener => listener(null));
}

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}
```

`src/feature/Auth/session/csrf.js`:

```js
export const CSRF_COOKIE = '__Host-csrf';

// Cookie CSRF cố ý KHÔNG HttpOnly: JS đọc rồi gửi lại trong header X-CSRF-Token.
export function readCsrfToken(cookieString = typeof document !== 'undefined' ? document.cookie : '') {
  const prefix = `${CSRF_COOKIE}=`;
  const match = cookieString
    .split(';')
    .map(part => part.trim())
    .find(part => part.indexOf(prefix) === 0);
  return match ? decodeURIComponent(match.substring(prefix.length)) : null;
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `CI=true npm test -- src/feature/Auth/session`
Expected: PASS 5/5.

- [ ] **Step 5: Commit**

```bash
git add src/feature/Auth/session
git commit -m "feat(fe-auth): in-memory access token store and CSRF cookie reader"
```

---

### Task 6.2: Gọi API auth, refresh single-flight đa tab, hẹn giờ refresh

**Files:**
- Create: `src/feature/Auth/api/authClient.js`
- Create: `src/feature/Auth/session/refreshCoordinator.js`
- Create: `src/feature/Auth/session/refreshScheduler.js`
- Test: `src/feature/Auth/session/__tests__/refreshCoordinator.test.js`, `src/feature/Auth/session/__tests__/refreshScheduler.test.js`

**Interfaces:**
- Consumes: `tokenStore`, `readCsrfToken` (6.1).
- Produces:
  - `authClient` (axios "trần", không interceptor): `login(email, password) → Promise<{accessToken, expiresAtUtc, mustChangePassword}>`, `refresh() → Promise<same>`, `logout() → Promise`.
  - `refreshCoordinator`: `refreshAccessToken() → Promise<string>` (single-flight trong tab; `navigator.locks` giữa các tab; tab khác vừa refresh thì dùng luôn token đó, không gọi mạng); `startAuthSync()`; `broadcastLogout()`; `onRemoteLogout(listener) → unsubscribe`.
  - `refreshScheduler`: `startRefreshScheduler(onFailure)`, `stopRefreshScheduler()` — gọi refresh lúc `expiresAt − 60s`.

- [ ] **Step 1: Viết test (đỏ)**

`src/feature/Auth/session/__tests__/refreshCoordinator.test.js`:

```js
import { refresh } from '../../api/authClient';
import { refreshAccessToken } from '../refreshCoordinator';
import { getAccessToken, setAccessToken, clearAccessToken } from '../tokenStore';

jest.mock('../../api/authClient', () => ({ refresh: jest.fn() }));

const inFifteenMinutes = () => new Date(Date.now() + 15 * 60 * 1000).toISOString();

beforeEach(() => {
  refresh.mockReset();
  clearAccessToken();
});

it('shares a single in-flight refresh between concurrent callers', async () => {
  let resolveRefresh;
  refresh.mockReturnValue(new Promise((resolve) => { resolveRefresh = resolve; }));

  const first = refreshAccessToken();
  const second = refreshAccessToken();
  resolveRefresh({ accessToken: 'new-token', expiresAtUtc: inFifteenMinutes() });

  await expect(first).resolves.toBe('new-token');
  await expect(second).resolves.toBe('new-token');
  expect(refresh).toHaveBeenCalledTimes(1);
  expect(getAccessToken()).toBe('new-token');
});

it('allows a new attempt after a failed refresh', async () => {
  refresh
    .mockRejectedValueOnce(new Error('401'))
    .mockResolvedValueOnce({ accessToken: 'ok', expiresAtUtc: inFifteenMinutes() });

  await expect(refreshAccessToken()).rejects.toThrow('401');
  await expect(refreshAccessToken()).resolves.toBe('ok');
});

it('does not call the network when another tab refreshed while we waited for the lock', async () => {
  const original = navigator.locks;
  Object.defineProperty(navigator, 'locks', {
    configurable: true,
    value: {
      request: (name, task) => {
        setAccessToken('from-other-tab', inFifteenMinutes());   // tab kia refresh + broadcast trong lúc ta chờ khoá
        return task();
      },
    },
  });

  await expect(refreshAccessToken()).resolves.toBe('from-other-tab');
  expect(refresh).not.toHaveBeenCalled();
  Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
});
```

`src/feature/Auth/session/__tests__/refreshScheduler.test.js`:

```js
import { refreshAccessToken } from '../refreshCoordinator';
import { startRefreshScheduler, stopRefreshScheduler } from '../refreshScheduler';
import { setAccessToken, clearAccessToken } from '../tokenStore';

jest.mock('../refreshCoordinator', () => ({ refreshAccessToken: jest.fn() }));
jest.useFakeTimers();

beforeEach(() => {
  refreshAccessToken.mockReset();
  refreshAccessToken.mockReturnValue(Promise.resolve('t'));
});

afterEach(() => {
  stopRefreshScheduler();
  clearAccessToken();
});

it('refreshes sixty seconds before the access token expires', () => {
  startRefreshScheduler(jest.fn());
  setAccessToken('a', new Date(Date.now() + 5 * 60 * 1000).toISOString());

  jest.advanceTimersByTime(3 * 60 * 1000 + 55 * 1000);
  expect(refreshAccessToken).not.toHaveBeenCalled();

  jest.advanceTimersByTime(10 * 1000);
  expect(refreshAccessToken).toHaveBeenCalledTimes(1);
});

it('reports a failed scheduled refresh', async () => {
  const onFailure = jest.fn();
  refreshAccessToken.mockReturnValue(Promise.reject(new Error('expired')));
  startRefreshScheduler(onFailure);
  setAccessToken('a', new Date(Date.now() + 30 * 1000).toISOString());   // đã trong vùng 60s ⇒ refresh ngay

  jest.advanceTimersByTime(0);
  await Promise.resolve();
  await Promise.resolve();

  expect(onFailure).toHaveBeenCalled();
});

it('does nothing after the token is cleared', () => {
  startRefreshScheduler(jest.fn());
  setAccessToken('a', new Date(Date.now() + 5 * 60 * 1000).toISOString());
  clearAccessToken();

  jest.advanceTimersByTime(10 * 60 * 1000);

  expect(refreshAccessToken).not.toHaveBeenCalled();
});
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `CI=true npm test -- src/feature/Auth/session`
Expected: FAIL — module chưa có.

- [ ] **Step 3: Viết code**

`src/feature/Auth/api/authClient.js`:

```js
import axios from 'axios';
import { readCsrfToken } from '../session/csrf';

// Client "trần" cho các endpoint dựa trên cookie — KHÔNG gắn interceptor refresh,
// nếu không refresh thất bại sẽ tự gọi refresh (vòng lặp).
const raw = axios.create({ baseURL: process.env.API_URL, withCredentials: true });

function csrfHeaders() {
  const token = readCsrfToken();
  return token ? { 'X-CSRF-Token': token } : {};
}

export function login(email, password) {
  return raw.post('v1/auth/login', { email, password }).then(response => response.data);
}

export function refresh() {
  return raw.post('v1/auth/refresh', null, { headers: csrfHeaders() }).then(response => response.data);
}

export function logout() {
  return raw.post('v1/auth/logout', null, { headers: csrfHeaders() });
}
```

`src/feature/Auth/session/refreshCoordinator.js`:

```js
import { refresh as refreshRequest } from '../api/authClient';
import {
  getAccessToken, getExpiresAt, setAccessToken, clearAccessToken,
} from './tokenStore';

// Server dùng strict reuse (Đặc tả kỹ thuật §4.3): hai request refresh cùng một cookie ⇒ cả phiên bị thu hồi.
// Vì vậy: 1 lời gọi/tab (single-flight) và 1 lời gọi/trình duyệt (Web Locks); tab xong thì phát token cho tab khác.
const LOCK_NAME = 'auth-refresh';
const CHANNEL_NAME = 'auth';
const FRESH_MARGIN_MS = 60 * 1000;

let inFlight = null;
let channel = null;
const remoteLogoutListeners = new Set();

function getChannel() {
  if (channel || typeof BroadcastChannel === 'undefined') return channel;
  channel = new BroadcastChannel(CHANNEL_NAME);
  channel.onmessage = (event) => {
    const message = event.data || {};
    if (message.type === 'token') setAccessToken(message.accessToken, message.expiresAtUtc);
    if (message.type === 'logout') {
      clearAccessToken();
      remoteLogoutListeners.forEach(listener => listener());
    }
  };
  return channel;
}

function runExclusive(task) {
  const locks = typeof navigator !== 'undefined' ? navigator.locks : undefined;
  // Trình duyệt không có Web Locks: chấp nhận rủi ro reuse giữa các tab (Đặc tả §4.3 đã nêu).
  return locks && locks.request ? locks.request(LOCK_NAME, task) : task();
}

function isFreshTokenFromElsewhere(tokenAtStart) {
  const token = getAccessToken();
  return Boolean(token) && token !== tokenAtStart && getExpiresAt() - Date.now() > FRESH_MARGIN_MS;
}

export function refreshAccessToken() {
  if (inFlight) return inFlight;
  const tokenAtStart = getAccessToken();

  inFlight = runExclusive(async () => {
    if (isFreshTokenFromElsewhere(tokenAtStart)) return getAccessToken();
    const data = await refreshRequest();
    setAccessToken(data.accessToken, data.expiresAtUtc);
    const ch = getChannel();
    if (ch) ch.postMessage({ type: 'token', accessToken: data.accessToken, expiresAtUtc: data.expiresAtUtc });
    return data.accessToken;
  }).then(
    (token) => { inFlight = null; return token; },
    (error) => { inFlight = null; throw error; },
  );
  return inFlight;
}

export function startAuthSync() {
  getChannel();
}

export function broadcastLogout() {
  const ch = getChannel();
  if (ch) ch.postMessage({ type: 'logout' });
}

export function onRemoteLogout(listener) {
  getChannel();
  remoteLogoutListeners.add(listener);
  return () => remoteLogoutListeners.delete(listener);
}
```

`src/feature/Auth/session/refreshScheduler.js`:

```js
import { subscribe, getExpiresAt } from './tokenStore';
import { refreshAccessToken } from './refreshCoordinator';

// Refresh chủ động trước khi access token hết hạn để người dùng đang thao tác không gặp lỗi.
const LEAD_MS = 60 * 1000;

let timer = null;
let unsubscribe = null;

function schedule(onFailure) {
  clearTimeout(timer);
  timer = null;
  const expiresAt = getExpiresAt();
  if (!expiresAt) return;
  const delay = Math.max(expiresAt - LEAD_MS - Date.now(), 0);
  timer = setTimeout(() => {
    refreshAccessToken().catch(onFailure);
  }, delay);
}

export function startRefreshScheduler(onFailure) {
  stopRefreshScheduler();
  unsubscribe = subscribe(() => schedule(onFailure));
  schedule(onFailure);
}

export function stopRefreshScheduler() {
  clearTimeout(timer);
  timer = null;
  if (unsubscribe) unsubscribe();
  unsubscribe = null;
}
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `CI=true npm test -- src/feature/Auth/session`
Expected: PASS 11/11.

- [ ] **Step 5: Commit**

```bash
git add src/feature/Auth
git commit -m "feat(fe-auth): single-flight cross-tab refresh coordinator and proactive refresh scheduler"
```

---

### Task 6.3: HTTP client — gắn token, 401 ⇒ refresh 1 lần rồi gửi lại, CSRF cho lệnh ghi

**Files:**
- Create: `src/service/http.js`
- Test: `src/service/__tests__/http.test.js`

**Interfaces:**
- Consumes: `getAccessToken` (6.1), `readCsrfToken` (6.1), `refreshAccessToken` (6.2).
- Produces: `createHttpClient(adapter?) → axios instance`; `default` = client dùng chung; `setSessionHandlers({ onSessionExpired, onPasswordChangeRequired })`.

- [ ] **Step 1: Viết test (đỏ)**

`src/service/__tests__/http.test.js`:

```js
import { createHttpClient, setSessionHandlers } from '../http';
import { refreshAccessToken } from '../../feature/Auth/session/refreshCoordinator';
import { setAccessToken, clearAccessToken } from '../../feature/Auth/session/tokenStore';

jest.mock('../../feature/Auth/session/refreshCoordinator', () => ({ refreshAccessToken: jest.fn() }));
jest.mock('../../feature/Auth/session/csrf', () => ({ readCsrfToken: () => 'csrf-1' }));

const future = () => new Date(Date.now() + 15 * 60 * 1000).toISOString();

// Adapter giả: trả lần lượt các response đã khai báo và ghi lại config của từng lần gọi.
function fakeBackend(...responses) {
  const calls = [];
  const adapter = (config) => {
    calls.push(config);
    const next = responses.shift();
    const response = { status: next.status, data: next.data || {}, headers: {}, config };
    return next.status >= 400
      ? Promise.reject(Object.assign(new Error(`HTTP ${next.status}`), { config, response }))
      : Promise.resolve(response);
  };
  return { adapter, calls };
}

let handlers;

beforeEach(() => {
  refreshAccessToken.mockReset();
  clearAccessToken();
  handlers = { onSessionExpired: jest.fn(), onPasswordChangeRequired: jest.fn() };
  setSessionHandlers(handlers);
});

it('attaches the bearer token and sends CSRF only for mutations', async () => {
  setAccessToken('a', future());
  const { adapter, calls } = fakeBackend({ status: 200 }, { status: 200 });
  const client = createHttpClient(adapter);

  await client.get('v1/auth/me');
  await client.post('v1/users', {});

  expect(calls[0].headers.Authorization).toBe('Bearer a');
  expect(calls[0].headers['X-CSRF-Token']).toBeUndefined();
  expect(calls[1].headers['X-CSRF-Token']).toBe('csrf-1');
});

it('on 401 refreshes once and replays the request with the new token', async () => {
  setAccessToken('old', future());
  refreshAccessToken.mockImplementation(() => {
    setAccessToken('new', future());
    return Promise.resolve('new');
  });
  const { adapter, calls } = fakeBackend({ status: 401 }, { status: 200, data: { ok: true } });

  const response = await createHttpClient(adapter).get('v1/users');

  expect(response.data.ok).toBe(true);
  expect(refreshAccessToken).toHaveBeenCalledTimes(1);
  expect(calls[1].headers.Authorization).toBe('Bearer new');
});

it('when refresh fails, reports the session as expired and rejects', async () => {
  refreshAccessToken.mockReturnValue(Promise.reject(new Error('refresh 401')));
  const { adapter } = fakeBackend({ status: 401 });

  await expect(createHttpClient(adapter).get('v1/users')).rejects.toBeDefined();
  expect(handlers.onSessionExpired).toHaveBeenCalledTimes(1);
});

it('never loops: a second 401 after the replay expires the session', async () => {
  refreshAccessToken.mockReturnValue(Promise.resolve('new'));
  const { adapter, calls } = fakeBackend({ status: 401 }, { status: 401 });

  await expect(createHttpClient(adapter).get('v1/users')).rejects.toBeDefined();
  expect(calls).toHaveLength(2);
  expect(refreshAccessToken).toHaveBeenCalledTimes(1);
  expect(handlers.onSessionExpired).toHaveBeenCalledTimes(1);
});

it('routes 403 password_change_required to its handler', async () => {
  const { adapter } = fakeBackend({ status: 403, data: { code: 'password_change_required' } });

  await expect(createHttpClient(adapter).get('v1/users')).rejects.toBeDefined();
  expect(handlers.onPasswordChangeRequired).toHaveBeenCalledTimes(1);
});
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `CI=true npm test -- src/service`
Expected: FAIL — module chưa có.

- [ ] **Step 3: Viết code**

`src/service/http.js`:

```js
import axios from 'axios';
import { getAccessToken } from '../feature/Auth/session/tokenStore';
import { refreshAccessToken } from '../feature/Auth/session/refreshCoordinator';
import { readCsrfToken } from '../feature/Auth/session/csrf';

const handlers = {
  onSessionExpired: () => {},
  onPasswordChangeRequired: () => {},
};

export function setSessionHandlers(next) {
  Object.assign(handlers, next);
}

// Client dùng cho mọi API cần đăng nhập. Tự gắn Bearer + CSRF, tự refresh 1 lần khi gặp 401.
// Gửi lại request sau 401 là an toàn kể cả với command: 401 xảy ra ở bước xác thực, trước khi nghiệp vụ chạy.
export function createHttpClient(adapter) {
  const client = axios.create({ baseURL: process.env.API_URL, withCredentials: true, adapter });

  client.interceptors.request.use((config) => {
    const headers = { ...config.headers };
    const token = getAccessToken();
    if (token) headers.Authorization = `Bearer ${token}`;
    const method = (config.method || 'get').toLowerCase();
    if (method !== 'get' && method !== 'head') {
      const csrf = readCsrfToken();
      if (csrf) headers['X-CSRF-Token'] = csrf;
    }
    return { ...config, headers };
  });

  client.interceptors.response.use(undefined, async (error) => {
    const { response, config } = error;
    if (!response || !config) throw error;

    if (response.status === 401 && !config.retriedAfterRefresh) {
      try {
        await refreshAccessToken();
      } catch (refreshError) {
        handlers.onSessionExpired();
        throw error;
      }
      // axios 0.18 đã ghép baseURL vào config.url ở lần gửi đầu — bỏ baseURL để không ghép 2 lần.
      return client({ ...config, baseURL: '', retriedAfterRefresh: true });
    }

    if (response.status === 401) handlers.onSessionExpired();
    if (response.status === 403 && response.data && response.data.code === 'password_change_required') {
      handlers.onPasswordChangeRequired();
    }
    throw error;
  });

  return client;
}

const http = createHttpClient();
export default http;
```

- [ ] **Step 4: Chạy test, xác nhận xanh**

Run: `CI=true npm test -- src/service`
Expected: PASS 5/5.

- [ ] **Step 5: Commit**

```bash
git add src/service
git commit -m "feat(fe): http client with bearer token, CSRF header and one-shot refresh on 401"
```

---

### Task 6.4: Redux auth, guard, `<Can>`, màn Login/Đổi mật khẩu, khởi động phiên

**Files:**
- Create in `src/feature/Auth/`: `redux/actionTypes.js`, `redux/reducer.js`, `redux/actions.js`, `permissions.js`, `problem.js`, `Can.js`, `PrivateRoute.js`, `Login.js`, `ChangePassword.js`, `index.js`
- Modify: `src/reducer.js` (tạo ở bước nền tảng 6.2), `src/index.js`
- Test: `src/feature/Auth/redux/__tests__/reducer.test.js`, `src/feature/Auth/redux/__tests__/actions.test.js`, `src/feature/Auth/__tests__/permissions.test.js`

**Interfaces:**
- Consumes: `authClient` (6.2), `refreshCoordinator` (6.2), `refreshScheduler` (6.2), `http`, `setSessionHandlers` (6.3), `tokenStore` (6.1). API: `GET v1/auth/me`, `POST v1/auth/change-password`.
- Produces:
  - Action type: `AUTH_BOOTING`, `AUTH_AUTHENTICATED`, `AUTH_ANONYMOUS`, `AUTH_LOGGED_OUT` (tiền tố `HMS/AUTH/`).
  - State `auth`: `{ status: 'booting'|'authenticated'|'anonymous', user, permissions: string[], mustChangePassword, sessionMessage }`.
  - Thunk: `bootAuth()`, `login(email, password)`, `logout(message?)`, `expireSession()`, `changePassword(currentPassword, newPassword)`, `loadMe()`.
  - `hasPermission(authState, code): boolean`; `<Can permission="...">`; `<PrivateRoute component={...} path=...>`; `problemTitle(error)`, `problemFieldErrors(error)`.

- [ ] **Step 1: Viết test (đỏ)**

`src/feature/Auth/redux/__tests__/reducer.test.js`:

```js
import authReducer from '../reducer';
import { AUTH_AUTHENTICATED, AUTH_LOGGED_OUT } from '../actionTypes';

const me = {
  id: 'u1', email: 'a@b.vn', fullName: 'A', avatarUrl: null,
  roles: [{ id: 'r1', code: 'doctor', name: 'Bác sĩ' }], permissions: ['users.read'], mustChangePassword: false,
};

it('starts in booting state', () => {
  expect(authReducer(undefined, { type: '@@INIT' }).status).toBe('booting');
});

it('stores user and permissions from /auth/me', () => {
  const state = authReducer(undefined, { type: AUTH_AUTHENTICATED, payload: me });

  expect(state.status).toBe('authenticated');
  expect(state.permissions).toEqual(['users.read']);
  expect(state.user.email).toBe('a@b.vn');
  expect(state.user.permissions).toBeUndefined();
});

it('clears everything on logout and keeps the reason message', () => {
  const loggedIn = authReducer(undefined, { type: AUTH_AUTHENTICATED, payload: me });

  const state = authReducer(loggedIn, { type: AUTH_LOGGED_OUT, payload: 'Phiên đã hết hiệu lực' });

  expect(state).toEqual({
    status: 'anonymous', user: null, permissions: [], mustChangePassword: false, sessionMessage: 'Phiên đã hết hiệu lực',
  });
});
```

`src/feature/Auth/redux/__tests__/actions.test.js`:

```js
import { bootAuth, logout } from '../actions';
import { AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_BOOTING, AUTH_LOGGED_OUT } from '../actionTypes';
import { refreshAccessToken, broadcastLogout } from '../../session/refreshCoordinator';
import { logout as logoutRequest } from '../../api/authClient';
import http from '../../../../service/http';
import { getAccessToken, setAccessToken } from '../../session/tokenStore';

jest.mock('../../session/refreshCoordinator', () => ({ refreshAccessToken: jest.fn(), broadcastLogout: jest.fn() }));
jest.mock('../../api/authClient', () => ({ login: jest.fn(), logout: jest.fn() }));
jest.mock('../../../../service/http', () => ({ get: jest.fn(), post: jest.fn() }));

const dispatchFor = (actions) => {
  const dispatch = (action) => (typeof action === 'function' ? action(dispatch) : actions.push(action));
  return dispatch;
};

it('bootAuth: refresh cookie works ⇒ loads /me and becomes authenticated', async () => {
  refreshAccessToken.mockResolvedValue('t');
  http.get.mockResolvedValue({ data: { id: 'u1', permissions: [], mustChangePassword: false } });
  const actions = [];

  await bootAuth()(dispatchFor(actions));

  expect(actions.map(a => a.type)).toEqual([AUTH_BOOTING, AUTH_AUTHENTICATED]);
});

it('bootAuth: no valid refresh cookie ⇒ anonymous', async () => {
  refreshAccessToken.mockRejectedValue(new Error('401'));
  const actions = [];

  await bootAuth()(dispatchFor(actions));

  expect(actions.map(a => a.type)).toEqual([AUTH_BOOTING, AUTH_ANONYMOUS]);
});

it('logout: clears the token, notifies other tabs even if the server call fails', async () => {
  setAccessToken('t', new Date(Date.now() + 60000).toISOString());
  logoutRequest.mockRejectedValue(new Error('offline'));
  const actions = [];

  await logout()(dispatchFor(actions));

  expect(getAccessToken()).toBeNull();
  expect(broadcastLogout).toHaveBeenCalled();
  expect(actions[0].type).toBe(AUTH_LOGGED_OUT);
});
```

`src/feature/Auth/__tests__/permissions.test.js`:

```js
import { hasPermission } from '../permissions';

it('checks the permission list of the auth slice', () => {
  const auth = { permissions: ['users.read'] };

  expect(hasPermission(auth, 'users.read')).toBe(true);
  expect(hasPermission(auth, 'roles.manage')).toBe(false);
  expect(hasPermission(undefined, 'users.read')).toBe(false);
});
```

- [ ] **Step 2: Chạy test, xác nhận đỏ**

Run: `CI=true npm test -- src/feature/Auth`
Expected: FAIL — module chưa có.

- [ ] **Step 3: Redux**

`src/feature/Auth/redux/actionTypes.js`:

```js
export const AUTH_BOOTING = 'HMS/AUTH/BOOTING';
export const AUTH_AUTHENTICATED = 'HMS/AUTH/AUTHENTICATED';
export const AUTH_ANONYMOUS = 'HMS/AUTH/ANONYMOUS';
export const AUTH_LOGGED_OUT = 'HMS/AUTH/LOGGED_OUT';
```

`src/feature/Auth/redux/reducer.js`:

```js
import {
  AUTH_BOOTING, AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_LOGGED_OUT,
} from './actionTypes';

// Redux chỉ giữ thông tin hiển thị. Token KHÔNG nằm ở đây (xem session/tokenStore.js).
const initialState = {
  status: 'booting',
  user: null,
  permissions: [],
  mustChangePassword: false,
  sessionMessage: null,
};

export default function authReducer(state = initialState, action) {
  switch (action.type) {
    case AUTH_BOOTING:
      return { ...initialState };
    case AUTH_AUTHENTICATED: {
      const { permissions, mustChangePassword, ...user } = action.payload;
      return {
        status: 'authenticated', user, permissions, mustChangePassword, sessionMessage: null,
      };
    }
    case AUTH_ANONYMOUS:
    case AUTH_LOGGED_OUT:
      return { ...initialState, status: 'anonymous', sessionMessage: action.payload || null };
    default:
      return state;
  }
}
```

`src/feature/Auth/redux/actions.js`:

```js
import http from '../../../service/http';
import * as authClient from '../api/authClient';
import { refreshAccessToken, broadcastLogout } from '../session/refreshCoordinator';
import { setAccessToken, clearAccessToken } from '../session/tokenStore';
import {
  AUTH_BOOTING, AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_LOGGED_OUT,
} from './actionTypes';

export const SESSION_EXPIRED_MESSAGE = 'Phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại.';

export const loadMe = () => async (dispatch) => {
  const { data } = await http.get('v1/auth/me');
  dispatch({ type: AUTH_AUTHENTICATED, payload: data });
  return data;
};

// Khởi động/F5: không có token trong RAM ⇒ thử refresh bằng cookie.
export const bootAuth = () => async (dispatch) => {
  dispatch({ type: AUTH_BOOTING });
  try {
    await refreshAccessToken();
    await dispatch(loadMe());
  } catch (error) {
    clearAccessToken();
    dispatch({ type: AUTH_ANONYMOUS });
  }
};

export const login = (email, password) => async (dispatch) => {
  const data = await authClient.login(email, password);
  setAccessToken(data.accessToken, data.expiresAtUtc);
  return dispatch(loadMe());
};

function endLocalSession(dispatch, message) {
  clearAccessToken();
  dispatch({ type: AUTH_LOGGED_OUT, payload: message || null });
}

export const logout = message => async (dispatch) => {
  try {
    await authClient.logout();
  } catch (error) {
    // Vẫn xoá phiên cục bộ dù server không phản hồi.
  }
  endLocalSession(dispatch, message);
  broadcastLogout();
};

export const expireSession = () => (dispatch) => {
  endLocalSession(dispatch, SESSION_EXPIRED_MESSAGE);
  broadcastLogout();
};

export const changePassword = (currentPassword, newPassword) => async (dispatch) => {
  const { data } = await http.post('v1/auth/change-password', { currentPassword, newPassword });
  setAccessToken(data.accessToken, data.expiresAtUtc);
  return dispatch(loadMe());
};
```

- [ ] **Step 4: Tiện ích quyền, lỗi, component**

`src/feature/Auth/permissions.js`:

```js
// Chỉ để ẩn/hiện UI. Quyết định thật nằm ở API.
export const hasPermission = (authState, code) => Boolean(
  authState && authState.permissions && authState.permissions.indexOf(code) !== -1,
);
```

`src/feature/Auth/problem.js`:

```js
// Đọc Problem Details (RFC 9457) từ lỗi axios.
export const problemTitle = error => (
  error && error.response && error.response.data && error.response.data.title
) || 'Không kết nối được máy chủ. Vui lòng thử lại.';

export const problemFieldErrors = error => (
  error && error.response && error.response.data && error.response.data.errors
) || {};
```

`src/feature/Auth/Can.js`:

```js
import { connect } from 'react-redux';
import { hasPermission } from './permissions';

const Can = ({ allowed, children }) => (allowed ? children : null);

export default connect((state, { permission }) => ({ allowed: hasPermission(state.auth, permission) }))(Can);
```

`src/feature/Auth/PrivateRoute.js`:

```js
import React from 'react';
import { connect } from 'react-redux';
import { Route, Redirect } from 'react-router-dom';

const PrivateRoute = ({
  component: Component, status, mustChangePassword, ...rest
}) => (
  <Route
    {...rest}
    render={(props) => {
      if (status === 'booting') return null;
      if (status !== 'authenticated') {
        return <Redirect to={{ pathname: '/login', state: { from: props.location } }} />;
      }
      if (mustChangePassword && props.location.pathname !== '/change-password') {
        return <Redirect to="/change-password" />;
      }
      return <Component {...props} />;
    }}
  />
);

export default connect(state => ({
  status: state.auth.status,
  mustChangePassword: state.auth.mustChangePassword,
}))(PrivateRoute);
```

`src/feature/Auth/Login.js`:

```js
import React from 'react';
import { connect } from 'react-redux';
import { Redirect } from 'react-router-dom';
import { login as loginAction } from './redux/actions';
import { problemTitle } from './problem';

class Login extends React.Component {
  state = {
    email: '', password: '', error: null, submitting: false,
  };

  handleChange = event => this.setState({ [event.target.name]: event.target.value });

  handleSubmit = async (event) => {
    event.preventDefault();
    this.setState({ submitting: true, error: null });
    try {
      await this.props.login(this.state.email, this.state.password);
    } catch (error) {
      // 401/429: hiện nguyên message server — server đã cố ý không phân biệt nguyên nhân.
      this.setState({ submitting: false, error: problemTitle(error) });
    }
  };

  render() {
    const {
      status, mustChangePassword, sessionMessage, location,
    } = this.props;
    if (status === 'authenticated') {
      const from = (location.state && location.state.from) || { pathname: '/' };
      return <Redirect to={mustChangePassword ? '/change-password' : from} />;
    }

    const {
      email, password, error, submitting,
    } = this.state;
    return (
      <form className="c-card" onSubmit={this.handleSubmit}>
        <h1 className="c-heading">Đăng nhập</h1>
        {sessionMessage && <p className="c-text--error">{sessionMessage}</p>}
        <input name="email" type="email" autoComplete="username" value={email} onChange={this.handleChange} placeholder="Email" required />
        <input name="password" type="password" autoComplete="current-password" value={password} onChange={this.handleChange} placeholder="Mật khẩu" required />
        {error && <p className="c-text--error">{error}</p>}
        <button type="submit" disabled={submitting}>Đăng nhập</button>
      </form>
    );
  }
}

export default connect(state => ({
  status: state.auth.status,
  mustChangePassword: state.auth.mustChangePassword,
  sessionMessage: state.auth.sessionMessage,
}), { login: loginAction })(Login);
```

`src/feature/Auth/ChangePassword.js`:

```js
import React from 'react';
import { connect } from 'react-redux';
import { changePassword as changePasswordAction } from './redux/actions';
import { problemTitle, problemFieldErrors } from './problem';

class ChangePassword extends React.Component {
  state = {
    currentPassword: '', newPassword: '', error: null, fieldErrors: {}, submitting: false,
  };

  handleChange = event => this.setState({ [event.target.name]: event.target.value });

  handleSubmit = async (event) => {
    event.preventDefault();
    this.setState({ submitting: true, error: null, fieldErrors: {} });
    try {
      await this.props.changePassword(this.state.currentPassword, this.state.newPassword);
      this.props.history.replace('/');
    } catch (error) {
      this.setState({ submitting: false, error: problemTitle(error), fieldErrors: problemFieldErrors(error) });
    }
  };

  render() {
    const {
      currentPassword, newPassword, error, fieldErrors, submitting,
    } = this.state;
    return (
      <form className="c-card" onSubmit={this.handleSubmit}>
        <h1 className="c-heading">Đổi mật khẩu</h1>
        {this.props.mustChangePassword && <p>Bạn cần đổi mật khẩu trước khi tiếp tục sử dụng hệ thống.</p>}
        <input name="currentPassword" type="password" autoComplete="current-password" value={currentPassword} onChange={this.handleChange} placeholder="Mật khẩu hiện tại" required />
        {fieldErrors.currentPassword && <p className="c-text--error">{fieldErrors.currentPassword[0]}</p>}
        <input name="newPassword" type="password" autoComplete="new-password" minLength={10} maxLength={128} value={newPassword} onChange={this.handleChange} placeholder="Mật khẩu mới (tối thiểu 10 ký tự)" required />
        {fieldErrors.newPassword && <p className="c-text--error">{fieldErrors.newPassword[0]}</p>}
        {error && !fieldErrors.currentPassword && !fieldErrors.newPassword && <p className="c-text--error">{error}</p>}
        <button type="submit" disabled={submitting}>Đổi mật khẩu</button>
      </form>
    );
  }
}

export default connect(state => ({ mustChangePassword: state.auth.mustChangePassword }),
  { changePassword: changePasswordAction })(ChangePassword);
```

`src/feature/Auth/index.js`:

```js
export { default as authReducer } from './redux/reducer';
export * from './redux/actions';
export * from './redux/actionTypes';
export { hasPermission } from './permissions';
export { default as Can } from './Can';
export { default as PrivateRoute } from './PrivateRoute';
export { default as Login } from './Login';
export { default as ChangePassword } from './ChangePassword';
```

- [ ] **Step 5: Chạy test, xác nhận xanh**

Run: `CI=true npm test -- src/feature/Auth src/service`
Expected: PASS toàn bộ (6.1–6.4).

- [ ] **Step 6: Nối vào ứng dụng**

`src/reducer.js` (tạo ở bước nền tảng 6.2) — reset toàn bộ state nghiệp vụ khi đăng xuất (Đặc tả §12.3: đăng xuất xoá state):

```js
import { combineReducers } from 'redux';
import { reducer as form } from 'redux-form';

// IMPORT_STORES
import { loadingReducer } from './feature/Loading';
import { authReducer, AUTH_LOGGED_OUT } from './feature/Auth';

const appReducer = combineReducers({
  // CONNECT_STORES
  auth: authReducer,
  form,
  loadingModal: loadingReducer,
});

export default (state, action) => appReducer(action.type === AUTH_LOGGED_OUT ? undefined : state, action);
```

⚠ Sau reset, reducer `auth` nhận lại `AUTH_LOGGED_OUT` từ state rỗng ⇒ ra `status: 'anonymous'` + `sessionMessage` — đúng ý.

`src/index.js` — sửa import và `<Switch>`, thêm khởi động phiên ngay sau khi tạo `store`:

```js
import {
  Login, ChangePassword, PrivateRoute, bootAuth, expireSession, AUTH_LOGGED_OUT,
} from './feature/Auth';
import { setSessionHandlers } from './service/http';
import { startAuthSync, onRemoteLogout } from './feature/Auth/session/refreshCoordinator';
import { startRefreshScheduler } from './feature/Auth/session/refreshScheduler';
```

```js
// Phiên đăng nhập: token trong RAM, refresh chủ động, đồng bộ đa tab.
setSessionHandlers({
  onSessionExpired: () => store.dispatch(expireSession()),
  onPasswordChangeRequired: () => history.push('/change-password'),
});
onRemoteLogout(() => store.dispatch({ type: AUTH_LOGGED_OUT }));
startAuthSync();
startRefreshScheduler(() => store.dispatch(expireSession()));
store.dispatch(bootAuth());
```

```jsx
          <Switch>
            <Route exact path="/login" component={Login} />
            <PrivateRoute exact path="/change-password" component={ChangePassword} />
            <PrivateRoute path="/" component={App} />
          </Switch>
```

(`App` là layout chính do các plan FE nghiệp vụ tạo; bỏ các route `Register`, `ForgotPassword`, `ResetPassword` — không có tự đăng ký/quên mật khẩu trong phạm vi.)

- [ ] **Step 7: Kiểm tra tay trên trình duyệt**

Chạy Postgres/Redis/Seq, API (profile `http`), Gateway (profile `http`), rồi `npm start` (`:9000`):

1. Mở `http://localhost:9000` → về `/login`. Đăng nhập Admin seed → bị chuyển `/change-password` → đổi xong vào `/`.
2. DevTools → Application → Cookies: có `__Host-rt` (HttpOnly ✓, Secure ✓, SameSite Strict) và `__Host-csrf`. Local/Session Storage: không có token.
3. F5 → vẫn đăng nhập (Network: 1 `POST /api/v1/auth/refresh` rồi `GET /api/v1/auth/me`).
4. Mở tab thứ 2. Để 15 phút (hoặc tạm đặt `Jwt:AccessTokenMinutes` = 2 cho dev) → Network mỗi chu kỳ chỉ **1** request refresh trên cả 2 tab; không tab nào bị đá ra.
5. Logout ở tab 1 → tab 2 tự về `/login`.
6. Chặn mạng tới `:5100` rồi thao tác → thông báo lỗi kết nối, không vòng lặp refresh.

- [ ] **Step 8: Commit**

```bash
git add src
git commit -m "feat(fe-auth): redux auth state, route guard, Can component, login and change-password screens, session bootstrap"
```

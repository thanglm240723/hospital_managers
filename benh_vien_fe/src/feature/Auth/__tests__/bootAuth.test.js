import { bootAuth, changePassword, logout } from 'feature/Auth/redux/actions';
import { AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_BOOTING } from 'feature/Auth/redux/actionTypes';
import reducer from 'feature/Auth/redux/reducer';
import { refresh, logout as logoutRequest } from 'feature/Auth/api/authClient';
import { getAccessToken } from 'feature/Auth/session/tokenStore';
import http from 'service/http';

jest.mock('feature/Auth/api/authClient', () => ({ refresh: jest.fn(), login: jest.fn(), logout: jest.fn() }));
jest.mock('service/http', () => ({ get: jest.fn(), post: jest.fn() }));

const fresh = () => new Date(Date.now() + 15 * 60 * 1000).toISOString();

function run(thunk) {
  const actions = [];
  const dispatch = action => (typeof action === 'function' ? action(dispatch) : actions.push(action));
  return { promise: thunk(dispatch), actions };
}

beforeEach(() => {
  jest.clearAllMocks();
  localStorage.clear();
});

it('cold boot: refresh → /me → authenticated; quyền và mustChangePassword lấy từ /me', async () => {
  refresh.mockResolvedValue({ accessToken: 'boot', expiresAtUtc: fresh(), mustChangePassword: true });
  const me = { id: 'u1', permissions: ['patients.read'], mustChangePassword: false };
  http.get.mockResolvedValue({ data: me });

  const { promise, actions } = run(bootAuth());
  await promise;

  expect(getAccessToken()).toBe('boot');
  expect(http.get).toHaveBeenCalledWith('v1/auth/me');
  expect(actions).toEqual([{ type: AUTH_BOOTING }, { type: AUTH_AUTHENTICATED, payload: me }]);
  const state = actions.reduce(reducer, undefined);
  expect(state.status).toBe('authenticated');
  expect(state.mustChangePassword).toBe(false);
  expect(state.permissions).toEqual(['patients.read']);
});

it('cold boot: refresh 401 → anonymous, không token, state về rỗng', async () => {
  refresh.mockRejectedValue(Object.assign(new Error('HTTP 401'), { response: { status: 401 } }));

  const { promise, actions } = run(bootAuth());
  await promise;

  expect(getAccessToken()).toBeNull();
  expect(http.get).not.toHaveBeenCalled();
  expect(actions.map(a => a.type)).toEqual([AUTH_BOOTING, AUTH_ANONYMOUS]);
  const prev = reducer(undefined, { type: AUTH_AUTHENTICATED, payload: { id: 'old', permissions: ['x'], mustChangePassword: false } });
  const state = reducer(prev, actions[1]);
  expect(state.status).toBe('anonymous');
  expect(state.user || null).toBeNull();
});

it('boot đang refresh thì logout: phản hồi đến muộn không được áp dụng', async () => {
  let resolveRefresh;
  refresh.mockReturnValue(new Promise((r) => { resolveRefresh = r; }));
  logoutRequest.mockResolvedValue({});

  const boot = run(bootAuth());
  await run(logout()).promise;
  resolveRefresh({ accessToken: 'late', expiresAtUtc: fresh(), mustChangePassword: false });
  await boot.promise;

  expect(getAccessToken()).toBeNull();
  expect(boot.actions.some(a => a.type === AUTH_AUTHENTICATED)).toBe(false);
});

it('change-password trả về sau logout: không đặt lại token (dùng chung epoch phiên)', async () => {
  refresh.mockResolvedValue({ accessToken: 'boot', expiresAtUtc: fresh(), mustChangePassword: true });
  http.get.mockResolvedValue({ data: { id: 'u1', permissions: [], mustChangePassword: true } });
  await run(bootAuth()).promise;
  let resolvePost;
  http.post.mockReturnValue(new Promise((r) => { resolvePost = r; }));
  logoutRequest.mockResolvedValue({});

  const change = run(changePassword('a', 'b'));
  await run(logout()).promise;
  resolvePost({ data: { accessToken: 'after-logout', expiresAtUtc: fresh() } });
  await change.promise.catch(() => {});

  expect(getAccessToken()).toBeNull();
});

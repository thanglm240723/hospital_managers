import { bootAuth, logout, login, loadMe, LOGOUT_UNCONFIRMED_MESSAGE } from '../actions';
import { AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_BOOTING, AUTH_LOGGED_OUT } from '../actionTypes';
import { refreshAccessToken, broadcastLogout } from '../../session/refreshCoordinator';
import { logout as logoutRequest, login as loginRequest } from '../../api/authClient';
import http from '../../../../service/http';
import { getAccessToken, setAccessToken } from '../../session/tokenStore';

jest.mock('../../session/refreshCoordinator', () => ({ refreshAccessToken: jest.fn(), broadcastLogout: jest.fn(), cancelPendingTokenRequests: jest.fn() }));
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

describe('logout và phản hồi đến muộn', () => {
  const deferred = () => {
    let resolve;
    const promise = new Promise((r) => {
      resolve = r;
    });
    return { promise, resolve };
  };

  beforeEach(() => jest.clearAllMocks());

  it('logout thành công: xoá phiên cục bộ, không hiện thông báo chưa xác nhận', async () => {
    logoutRequest.mockResolvedValue({ status: 204 });
    const actions = [];

    await logout()(dispatchFor(actions));

    expect(actions).toEqual([{ type: AUTH_LOGGED_OUT, payload: null }]);
    expect(logoutRequest).toHaveBeenCalledTimes(1);
  });

  it('logout server lỗi: vẫn xoá cục bộ, báo chưa xác nhận thu hồi, không retry', async () => {
    setAccessToken('t', new Date(Date.now() + 60000).toISOString());
    logoutRequest.mockRejectedValue(new Error('network'));
    const actions = [];

    await logout()(dispatchFor(actions));

    expect(getAccessToken()).toBeNull();
    expect(logoutRequest).toHaveBeenCalledTimes(1);
    expect(actions[actions.length - 1]).toEqual({ type: AUTH_LOGGED_OUT, payload: LOGOUT_UNCONFIRMED_MESSAGE });
    expect(LOGOUT_UNCONFIRMED_MESSAGE).toBe('Đã thoát trên thiết bị này; chưa xác nhận thu hồi phiên trên máy chủ.');
    expect(actions.some(a => a.type === AUTH_AUTHENTICATED)).toBe(false);
  });

  it('login đang chờ trả về sau logout: không set token, không authenticated', async () => {
    const pending = deferred();
    loginRequest.mockReturnValue(pending.promise);
    logoutRequest.mockResolvedValue({});
    const actions = [];
    const dispatch = dispatchFor(actions);

    const loginPromise = login('a@b.com', 'x')(dispatch);
    await logout()(dispatch);
    pending.resolve({ accessToken: 'late', expiresAtUtc: new Date(Date.now() + 600000).toISOString() });
    await loginPromise;

    expect(getAccessToken()).toBeNull();
    expect(http.get).not.toHaveBeenCalled();
    expect(actions.some(a => a.type === AUTH_AUTHENTICATED)).toBe(false);
  });

  it('/me đang chờ trả về sau logout: không authenticated', async () => {
    const pending = deferred();
    http.get.mockReturnValue(pending.promise);
    logoutRequest.mockResolvedValue({});
    const actions = [];
    const dispatch = dispatchFor(actions);

    const mePromise = dispatch(loadMe());
    await logout()(dispatch);
    pending.resolve({ data: { id: 'u1', permissions: [], mustChangePassword: false } });
    await mePromise;

    expect(actions.some(a => a.type === AUTH_AUTHENTICATED)).toBe(false);
  });
});

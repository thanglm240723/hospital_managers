import rootReducer from 'reducer';
import { AUTH_AUTHENTICATED, AUTH_LOGGED_OUT } from 'feature/Auth/redux/actionTypes';
import { setAccessToken, getAccessToken } from 'feature/Auth/session/tokenStore';
import { startRefreshScheduler, stopRefreshScheduler } from 'feature/Auth/session/refreshScheduler';
import { invalidateLocalSession } from 'feature/Auth/session/sessionLifecycle';

jest.mock('feature/Auth/session/refreshCoordinator', () => ({ refreshAccessToken: jest.fn(() => Promise.resolve('t')) }));

it('AUTH_LOGGED_OUT đưa toàn bộ root state (auth, workspace, dữ liệu feature) về ban đầu', () => {
  const initial = rootReducer(undefined, { type: '@@INIT' });
  const dirty = Object.keys(initial).reduce((acc, key) => ({ ...acc, [key]: { leaked: 'phi' } }), {});
  const loggedIn = rootReducer(dirty, { type: AUTH_AUTHENTICATED, payload: { id: 'u1', permissions: ['x'], mustChangePassword: false } });

  const after = rootReducer(loggedIn, { type: AUTH_LOGGED_OUT, payload: 'msg' });

  Object.keys(initial)
    .filter(key => key !== 'auth')
    .forEach((key) => {
      expect(after[key]).toEqual(initial[key]);
    });
  expect(after.auth.status).toBe('anonymous');
  expect(after.auth.user).toBeNull();
  expect(after.auth.permissions).toEqual([]);
  expect(after.auth.sessionMessage).toBe('msg');
});

it('xoá phiên cục bộ (logout/remote logout) xoá token RAM và ngừng timer refresh', () => {
  jest.useFakeTimers();
  const { refreshAccessToken } = jest.requireMock('feature/Auth/session/refreshCoordinator');
  try {
    setAccessToken('t', new Date(Date.now() + 2 * 60 * 1000).toISOString());
    startRefreshScheduler(jest.fn());

    invalidateLocalSession();
    jest.advanceTimersByTime(10 * 60 * 1000);

    expect(getAccessToken()).toBeNull();
    expect(refreshAccessToken).not.toHaveBeenCalled();
  } finally {
    stopRefreshScheduler();
    jest.useRealTimers();
  }
});

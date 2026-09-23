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

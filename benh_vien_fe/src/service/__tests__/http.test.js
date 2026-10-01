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

describe('nhiều request cùng gặp 401', () => {
  const originalApiUrl = process.env.API_URL;

  afterEach(() => {
    process.env.API_URL = originalApiUrl;
  });

  it('10 request dùng chung một promise refresh, mỗi request retry tối đa một lần và giữ nguyên headers/body/Idempotency-Key', async () => {
    process.env.API_URL = 'http://gw.local/api/';
    setAccessToken('old', future());
    let resolveRefresh;
    const shared = new Promise((resolve) => { resolveRefresh = resolve; });
    refreshAccessToken.mockImplementation(() => shared);

    const calls = [];
    const adapter = (config) => {
      calls.push(config);
      const ok = config.headers.Authorization === 'Bearer new';
      const response = { status: ok ? 200 : 401, data: { ok }, headers: {}, config };
      return ok ? Promise.resolve(response) : Promise.reject(Object.assign(new Error('HTTP 401'), { config, response }));
    };
    const client = createHttpClient(adapter);

    const requests = Array.from({ length: 10 }, (_, i) => client.post(`v1/items/${i}/call-next`, { n: i }, { headers: { 'Idempotency-Key': `key-${i}` } }));
    await new Promise(resolve => setTimeout(resolve, 0));
    setAccessToken('new', future());
    resolveRefresh('new');
    const responses = await Promise.all(requests);

    expect(responses.every(r => r.data.ok)).toBe(true);
    expect(calls).toHaveLength(20);
    const replays = calls.slice(10);
    replays.forEach((config) => {
      const i = Number(config.url.match(/items\/(\d+)\//)[1]);
      expect(config.url).toBe(`http://gw.local/api/v1/items/${i}/call-next`);
      expect(config.headers['Idempotency-Key']).toBe(`key-${i}`);
      expect(config.headers['X-CSRF-Token']).toBe('csrf-1');
      expect(JSON.parse(config.data)).toEqual({ n: i });
    });
    expect(new Set(replays.map(c => c.url)).size).toBe(10);
    expect(handlers.onSessionExpired).not.toHaveBeenCalled();
  });
});

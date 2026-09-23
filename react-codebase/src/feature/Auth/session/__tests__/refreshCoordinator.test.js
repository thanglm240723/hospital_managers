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

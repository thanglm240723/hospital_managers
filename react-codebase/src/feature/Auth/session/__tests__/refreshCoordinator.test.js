import { refresh } from '../../api/authClient';
import { refreshAccessToken } from '../refreshCoordinator';
import { getAccessToken, setAccessToken, clearAccessToken } from '../tokenStore';

jest.mock('../../api/authClient', () => ({ refresh: jest.fn() }));

const GEN_KEY = 'auth:refreshGen';
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

  try {
    await expect(refreshAccessToken()).resolves.toBe('from-other-tab');
    expect(refresh).not.toHaveBeenCalled();
  } finally {
    Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
  }
});

describe('cross-tab generation counter (fix round 1)', () => {
  beforeEach(() => {
    try { localStorage.removeItem(GEN_KEY); } catch { /* ignore */ }
  });

  afterEach(() => {
    try { localStorage.removeItem(GEN_KEY); } catch { /* ignore */ }
  });

  it('increments the non-secret generation counter after each successful network refresh', async () => {
    refresh.mockResolvedValueOnce({ accessToken: 'g1', expiresAtUtc: inFifteenMinutes() });
    await refreshAccessToken();
    expect(localStorage.getItem(GEN_KEY)).toBe('1');

    clearAccessToken();
    refresh.mockResolvedValueOnce({ accessToken: 'g2', expiresAtUtc: inFifteenMinutes() });
    await refreshAccessToken();
    expect(localStorage.getItem(GEN_KEY)).toBe('2');
  });

  it('fails instead of resending the cookie when the lock reveals another tab already refreshed and no token turns up', async () => {
    localStorage.setItem(GEN_KEY, '5');
    const original = navigator.locks;
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: (name, task) => {
          // Simulate another tab finishing its refresh (bumping the counter) while we waited
          // for the lock, with no `token` broadcast delivered to this tab at all.
          localStorage.setItem(GEN_KEY, '6');
          return task();
        },
      },
    });

    try {
      await expect(refreshAccessToken()).rejects.toThrow();
      expect(refresh).not.toHaveBeenCalled();
    } finally {
      Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
    }
  });

  it('gets the lock before the winner\'s broadcast is delivered, but answers via need-token instead of resending the cookie', async () => {
    // Reproduces the review's Important #1 race: the waiter is granted the Web Lock before the
    // winner's `token` BroadcastChannel message lands. The generation counter (already bumped by
    // the winner, read synchronously inside the lock) lets the waiter detect this and ask over
    // the channel instead of calling refresh() again with the already-consumed cookie.
    class FakeChannel {
      constructor() { this.onmessage = null; }

      postMessage(message) {
        if (message.type === 'need-token') {
          // Answered as if the winner tab (or its late-arriving broadcast) responds.
          setAccessToken('winner-token', inFifteenMinutes());
        }
      }

      close() {}
    }
    const originalBroadcastChannel = global.BroadcastChannel;
    global.BroadcastChannel = FakeChannel;

    localStorage.setItem(GEN_KEY, '10');
    const original = navigator.locks;
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: (name, task) => {
          // Winner already incremented the generation counter, but its `token` broadcast has
          // not reached this tab yet when the lock is granted to us.
          localStorage.setItem(GEN_KEY, '11');
          return task();
        },
      },
    });

    try {
      await expect(refreshAccessToken()).resolves.toBe('winner-token');
      expect(refresh).not.toHaveBeenCalled();
    } finally {
      Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
      global.BroadcastChannel = originalBroadcastChannel;
    }
  });

  it('falls back to a normal single network refresh when localStorage is unavailable', async () => {
    const originalGetItem = Storage.prototype.getItem;
    const originalSetItem = Storage.prototype.setItem;
    Storage.prototype.getItem = () => { throw new Error('storage blocked'); };
    Storage.prototype.setItem = () => { throw new Error('storage blocked'); };

    try {
      refresh.mockResolvedValueOnce({ accessToken: 'fallback-token', expiresAtUtc: inFifteenMinutes() });
      await expect(refreshAccessToken()).resolves.toBe('fallback-token');
      expect(refresh).toHaveBeenCalledTimes(1);
    } finally {
      Storage.prototype.getItem = originalGetItem;
      Storage.prototype.setItem = originalSetItem;
    }
  });
});

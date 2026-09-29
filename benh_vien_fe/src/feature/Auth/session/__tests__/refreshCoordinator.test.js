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
        setAccessToken('from-other-tab', inFifteenMinutes()); // tab kia refresh + broadcast trong lúc ta chờ khoá
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

describe('cross-tab generation counter (fix rounds 1 & 2)', () => {
  // refreshCoordinator caches its BroadcastChannel instance the first time it is successfully
  // created and never re-checks `typeof BroadcastChannel`, so swapping global.BroadcastChannel
  // per-test (as fix round 1 did) silently leaves earlier tests' mock wired up. Instead, install
  // ONE TestChannel for this whole describe block and let each test steer its "response" to a
  // `need-token` message through the shared, per-test-reset `needTokenResponder` closure.
  let needTokenResponder = null;

  class TestChannel {
    constructor() { this.onmessage = null; }

    postMessage(message) {
      if (message.type === 'need-token' && needTokenResponder) needTokenResponder();
    }

    close() {}
  }

  let originalBroadcastChannel;

  beforeAll(() => {
    originalBroadcastChannel = global.BroadcastChannel;
    global.BroadcastChannel = TestChannel;
  });

  afterAll(() => {
    global.BroadcastChannel = originalBroadcastChannel;
  });

  beforeEach(() => {
    needTokenResponder = null;
    try { localStorage.removeItem(GEN_KEY); } catch { /* ignore */ }
  });

  afterEach(() => {
    try { localStorage.removeItem(GEN_KEY); } catch { /* ignore */ }
  });

  // Establishes this tab's own remembered token generation (myTokenGen) via a real, successful
  // refresh — required as a baseline before the coordinator can ever conclude "another tab
  // already refreshed" from a counter mismatch (fix round 2).
  async function establishOwnGeneration(token) {
    refresh.mockResolvedValueOnce({ accessToken: token, expiresAtUtc: inFifteenMinutes() });
    await refreshAccessToken();
    refresh.mockReset();
  }

  function bumpGenExternally() {
    localStorage.setItem(GEN_KEY, String(Number(localStorage.getItem(GEN_KEY) || '0') + 1));
  }

  it('increments the non-secret generation counter after each successful network refresh', async () => {
    refresh.mockResolvedValueOnce({ accessToken: 'g1', expiresAtUtc: inFifteenMinutes() });
    await refreshAccessToken();
    expect(localStorage.getItem(GEN_KEY)).toBe('1');

    clearAccessToken();
    refresh.mockResolvedValueOnce({ accessToken: 'g2', expiresAtUtc: inFifteenMinutes() });
    await refreshAccessToken();
    expect(localStorage.getItem(GEN_KEY)).toBe('2');
  });

  it('fails instead of resending the cookie when the counter moved ahead of our remembered generation and no token turns up', async () => {
    await establishOwnGeneration('mine'); // myTokenGen now tracks generation 1
    // needTokenResponder stays null: no other tab answers the need-token request.

    const original = navigator.locks;
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: (name, task) => {
          // Another tab refreshes concurrently and bumps the counter while we wait for the
          // lock; its broadcast never reaches us.
          bumpGenExternally();
          return task();
        },
      },
    });

    try {
      await expect(refreshAccessToken()).rejects.toThrow();
      expect(refresh).not.toHaveBeenCalled(); // no resend of the consumed cookie
    } finally {
      Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
    }
  });

  it('gets the lock before the winner\'s broadcast is delivered, but answers via need-token instead of resending the cookie', async () => {
    // Reproduces the review's Important #1 race: the waiter is granted the Web Lock before the
    // winner's `token` BroadcastChannel message lands. The generation counter (already bumped by
    // the winner, read synchronously inside the lock) lets the waiter detect this and ask over
    // the channel instead of calling refresh() again with the already-consumed cookie.
    await establishOwnGeneration('mine'); // myTokenGen now tracks generation 1
    needTokenResponder = () => setAccessToken('winner-token', inFifteenMinutes());

    const original = navigator.locks;
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: (name, task) => {
          // Winner (a different tab) already incremented the generation counter, but its
          // `token` broadcast has not reached this tab yet when the lock is granted to us.
          bumpGenExternally();
          return task();
        },
      },
    });

    try {
      await expect(refreshAccessToken()).resolves.toBe('winner-token');
      expect(refresh).not.toHaveBeenCalled();
    } finally {
      Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
    }
  });

  it('waits for the actual new token instead of returning the stale one already in memory (proactive refresh)', async () => {
    // Review's Important #1 (round 2): during a proactive refresh, the OLD token is still in
    // memory when the need-token path is entered. waitForTokenFromOtherTabs must not resolve
    // with that stale token — it has to keep waiting until a genuinely NEW, fresh token shows up.
    await establishOwnGeneration('old-token'); // myTokenGen tracks gen 1, 'old-token' in memory
    needTokenResponder = () => {
      // The winner's answer arrives a little later than the request.
      setTimeout(() => setAccessToken('new-token', inFifteenMinutes()), 30);
    };

    const original = navigator.locks;
    Object.defineProperty(navigator, 'locks', {
      configurable: true,
      value: {
        request: (name, task) => {
          // Another tab refreshed concurrently and bumped the counter; its broadcast hasn't
          // reached us yet, so 'old-token' is still what's in memory right now.
          bumpGenExternally();
          return task();
        },
      },
    });

    try {
      await expect(refreshAccessToken()).resolves.toBe('new-token');
      expect(refresh).not.toHaveBeenCalled(); // only the establishing call happened, no resend
    } finally {
      Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
    }
  });

  it('detects a counter bump after the lock is already free (broadcast not yet delivered) and asks other tabs instead of the network', async () => {
    // Review's Important #2 (round 2 residual window): the winner writes the counter, broadcasts
    // and releases the lock entirely before this tab even starts its own refreshAccessToken()
    // call. There is no lock contention to reproduce — a stale call-time snapshot would be the
    // whole bug. myTokenGen (the remembered generation, not a fresh read at call time) is what
    // must catch this.
    await establishOwnGeneration('old-token'); // myTokenGen tracks gen 1
    needTokenResponder = () => setAccessToken('new-token', inFifteenMinutes());

    // The other tab's whole refresh (network call, counter bump, broadcast, lock release)
    // already happened; only its broadcast has not reached us.
    bumpGenExternally();

    await expect(refreshAccessToken()).resolves.toBe('new-token');
    expect(refresh).not.toHaveBeenCalled(); // only the establishing call happened, no resend
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

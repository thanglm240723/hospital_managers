import { refresh } from '../../api/authClient';
import { refreshAccessToken } from '../refreshCoordinator';
import { getAccessToken, setAccessToken, clearAccessToken } from '../tokenStore';
import { beginSessionTransition } from '../sessionLifecycle';

jest.mock('../../api/authClient', () => ({ refresh: jest.fn() }));

const GEN_KEY = 'auth:refreshGen';
const inFifteenMinutes = () => new Date(Date.now() + 15 * 60 * 1000).toISOString();

beforeEach(() => {
  refresh.mockReset();
  clearAccessToken();
  beginSessionTransition();
});

it('10 lời gọi đồng thời trong cùng tab chỉ gọi mạng một lần', async () => {
  let resolveRefresh;
  refresh.mockReturnValue(new Promise((resolve) => { resolveRefresh = resolve; }));

  const calls = Array.from({ length: 10 }, () => refreshAccessToken());
  resolveRefresh({ accessToken: 'one', expiresAtUtc: inFifteenMinutes() });

  await expect(Promise.all(calls)).resolves.toEqual(Array(10).fill('one'));
  expect(refresh).toHaveBeenCalledTimes(1);
});

it('trình duyệt thiếu Web Locks: vẫn single-flight trong tab (rủi ro giữa các tab đã ghi trong spec)', async () => {
  const original = navigator.locks;
  Object.defineProperty(navigator, 'locks', { configurable: true, value: undefined });
  try {
    refresh.mockResolvedValueOnce({ accessToken: 'nolock', expiresAtUtc: inFifteenMinutes() });
    const [a, b] = await Promise.all([refreshAccessToken(), refreshAccessToken()]);
    expect([a, b]).toEqual(['nolock', 'nolock']);
    expect(refresh).toHaveBeenCalledTimes(1);
  } finally {
    Object.defineProperty(navigator, 'locks', { configurable: true, value: original });
  }
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

describe('broadcast token cũ sau logout', () => {
  let instances;
  let modules;

  class RecordingChannel {
    constructor() {
      this.onmessage = null;
      this.sent = [];
      instances.push(this);
    }

    postMessage(message) {
      this.sent.push(message);
    }

    close() {}
  }

  const deliver = message => instances[0].onmessage({ data: message });
  const fresh = () => new Date(Date.now() + 15 * 60 * 1000).toISOString();

  beforeEach(() => {
    instances = [];
    localStorage.clear();
    global.BroadcastChannel = RecordingChannel;
    jest.resetModules();
    modules = {
      coordinator: require('../refreshCoordinator'), // eslint-disable-line global-require
      store: require('../tokenStore'), // eslint-disable-line global-require
      lifecycle: require('../sessionLifecycle'), // eslint-disable-line global-require
      client: require('../../api/authClient'), // eslint-disable-line global-require
    };
    modules.coordinator.startAuthSync();
  });

  afterEach(() => {
    delete global.BroadcastChannel;
  });

  const gen = () => modules.lifecycle.getSessionGeneration();

  it('remote logout xoá token và bỏ qua token tự phát đến sau đó', () => {
    const listener = jest.fn();
    modules.lifecycle.beginSessionTransition();
    const sessionGen = gen();
    modules.coordinator.onRemoteLogout(listener);
    modules.store.setAccessToken('mine', fresh());

    deliver({ type: 'logout', sessionGen });
    deliver({ type: 'token', sessionGen, accessToken: 'stale', expiresAtUtc: fresh() });

    expect(listener).toHaveBeenCalledTimes(1);
    expect(modules.store.getAccessToken()).toBeNull();
  });

  it('phản hồi need-token có requestId không khớp bị bỏ qua', () => {
    modules.lifecycle.beginSessionTransition();

    deliver({ type: 'token', requestId: 'khong-khop', accessToken: 'x', expiresAtUtc: fresh() });

    expect(modules.store.getAccessToken()).toBeNull();
  });

  it('token broadcast khi phiên đang hoạt động vẫn được nhận', () => {
    modules.lifecycle.beginSessionTransition();

    deliver({ type: 'token', sessionGen: gen(), accessToken: 'ok', expiresAtUtc: fresh() });

    expect(modules.store.getAccessToken()).toBe('ok');
  });

  it('token broadcast không mang thế hệ phiên bị bỏ qua', () => {
    modules.lifecycle.beginSessionTransition();

    deliver({ type: 'token', accessToken: 'no-context', expiresAtUtc: fresh() });

    expect(modules.store.getAccessToken()).toBeNull();
  });

  it('token broadcast từ phiên khác (tab khác đăng nhập tài khoản khác) bị bỏ qua', () => {
    modules.lifecycle.beginSessionTransition();
    const oldGen = gen();
    modules.store.setAccessToken('mine', fresh());
    // Tab khác đăng nhập: thế hệ phiên dùng chung tăng; tab này vẫn ở thế hệ cũ.
    localStorage.setItem('auth:sessionGen', 'other-tab-gen');

    deliver({ type: 'token', sessionGen: 'other-tab-gen', accessToken: 'foreign', expiresAtUtc: fresh() });
    deliver({ type: 'token', sessionGen: oldGen, accessToken: 'stale-gen', expiresAtUtc: fresh() });

    expect(modules.store.getAccessToken()).toBe('mine');
  });

  it('logout broadcast từ phiên khác không đăng xuất tab này', () => {
    const listener = jest.fn();
    modules.lifecycle.beginSessionTransition();
    modules.coordinator.onRemoteLogout(listener);
    modules.store.setAccessToken('mine', fresh());

    deliver({ type: 'logout', sessionGen: 'older-gen' });

    expect(listener).not.toHaveBeenCalled();
    expect(modules.store.getAccessToken()).toBe('mine');
  });

  it('hai lần claimSharedSession liên tiếp cho thế hệ khác nhau', () => {
    modules.lifecycle.claimSharedSession();
    const first = gen();
    modules.lifecycle.claimSharedSession();

    expect(first).toBeTruthy();
    expect(gen()).toBeTruthy();
    expect(gen()).not.toBe(first);
    expect(localStorage.getItem('auth:sessionGen')).toBe(gen());
  });

  it('token phát sau refresh mang thế hệ phiên hiện tại', async () => {
    modules.client.refresh.mockResolvedValue({ accessToken: 'r1', expiresAtUtc: fresh() });
    modules.lifecycle.beginSessionTransition();

    await modules.coordinator.refreshAccessToken();

    const sent = instances[0].sent.find(m => m.type === 'token');
    expect(sent.sessionGen).toBe(gen());
    expect(sent.sessionGen).not.toBeNull();
  });

  it('need-token chỉ được trả lời cho tab cùng thế hệ phiên', () => {
    modules.lifecycle.beginSessionTransition();
    modules.store.setAccessToken('mine', fresh());

    deliver({ type: 'need-token', requestId: 'r', sessionGen: 'newer-gen' });
    expect(instances[0].sent.some(m => m.type === 'token')).toBe(false);

    deliver({ type: 'need-token', requestId: 'r', sessionGen: gen() });
    expect(instances[0].sent.filter(m => m.type === 'token')).toEqual([
      expect.objectContaining({ requestId: 'r', sessionGen: gen(), accessToken: 'mine' }),
    ]);
  });

  it('refresh trả về sau khi tab khác đăng nhập tài khoản khác: không áp dụng, không phát', async () => {
    let resolveRefresh;
    modules.client.refresh.mockReturnValue(new Promise((r) => { resolveRefresh = r; }));
    modules.lifecycle.beginSessionTransition();

    const pending = modules.coordinator.refreshAccessToken();
    localStorage.setItem('auth:sessionGen', 'other-tab-gen');
    resolveRefresh({ accessToken: 'other-account', expiresAtUtc: fresh() });

    await expect(pending).rejects.toThrow();
    expect(modules.store.getAccessToken()).toBeNull();
    expect(instances[0].sent.some(m => m.type === 'token')).toBe(false);
  });

  it('tab mới chưa có token: gọi mạng đúng một lần dù bộ đếm refresh đã > 0', async () => {
    localStorage.setItem('auth:refreshGen', '7');
    modules.client.refresh.mockResolvedValue({ accessToken: 'cold', expiresAtUtc: fresh() });
    modules.lifecycle.beginSessionTransition();

    await expect(modules.coordinator.refreshAccessToken()).resolves.toBe('cold');
    expect(modules.client.refresh).toHaveBeenCalledTimes(1);
    expect(localStorage.getItem('auth:refreshGen')).toBe('8');
  });

  it('refresh trả về sau logout: không áp dụng token, không phát token', async () => {
    let resolveRefresh;
    modules.client.refresh.mockReturnValue(
      new Promise((r) => {
        resolveRefresh = r;
      }),
    );
    modules.lifecycle.beginSessionTransition();

    const pending = modules.coordinator.refreshAccessToken();
    modules.lifecycle.invalidateLocalSession();
    resolveRefresh({ accessToken: 'late', expiresAtUtc: fresh() });

    await expect(pending).rejects.toThrow('session ended');
    expect(modules.store.getAccessToken()).toBeNull();
    expect(instances[0].sent.some(m => m.type === 'token')).toBe(false);
  });
});

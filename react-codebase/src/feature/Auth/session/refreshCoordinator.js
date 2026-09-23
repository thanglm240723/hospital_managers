import { refresh as refreshRequest } from '../api/authClient';
import {
  getAccessToken, getExpiresAt, setAccessToken, clearAccessToken,
} from './tokenStore';

// Server dùng strict reuse (Đặc tả kỹ thuật §4.3): hai request refresh cùng một cookie ⇒ cả phiên bị thu hồi.
// Vì vậy: 1 lời gọi/tab (single-flight) và 1 lời gọi/trình duyệt (Web Locks); tab xong thì phát token cho tab khác.
const LOCK_NAME = 'auth-refresh';
const CHANNEL_NAME = 'auth';
const FRESH_MARGIN_MS = 60 * 1000;
// Fix round 1: BroadcastChannel delivery is not ordered against the Web Lock hand-off — a
// waiting tab can be granted the lock before the winner's `token` message arrives. That tab
// would then resend the already-consumed __Host-rt and trigger strict-reuse revocation of the
// whole family. A non-secret generation counter in localStorage (never the token itself) lets a
// tab detect, synchronously inside the lock, that another tab already refreshed — even if the
// broadcast has not landed yet — so it can wait for the token instead of hitting the network.
const GEN_KEY = 'auth:refreshGen';
const NEED_TOKEN_TIMEOUT_MS = 1000;

let inFlight = null;
let channel = null;
const remoteLogoutListeners = new Set();

// localStorage access is best-effort: if it throws (privacy mode, storage disabled, quota) or is
// absent, we fall back to the pre-fix behaviour (rely solely on the in-memory token check).
function readGen() {
  try {
    if (typeof localStorage === 'undefined') return null;
    const raw = localStorage.getItem(GEN_KEY);
    const parsed = parseInt(raw, 10);
    return Number.isFinite(parsed) ? parsed : 0;
  } catch {
    return null;
  }
}

function writeGen(next) {
  try {
    if (typeof localStorage === 'undefined') return;
    localStorage.setItem(GEN_KEY, String(next));
  } catch {
    // Best effort only — a non-secret counter, so losing an increment just means the next
    // tab falls back to the token/broadcast check instead of skipping the network call.
  }
}

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
    if (message.type === 'need-token') {
      const token = getAccessToken();
      const expiresAt = getExpiresAt();
      if (token && expiresAt && expiresAt - Date.now() > FRESH_MARGIN_MS) {
        channel.postMessage({ type: 'token', accessToken: token, expiresAtUtc: new Date(expiresAt).toISOString() });
      }
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

// Ask other tabs for the token they already hold, with a short timeout. Used only when the
// generation counter proves another tab already refreshed but its broadcast has not (yet)
// updated our in-memory token store.
function waitForTokenFromOtherTabs() {
  const ch = getChannel();
  if (!ch) return Promise.resolve(getAccessToken());
  ch.postMessage({ type: 'need-token' });
  return new Promise((resolve) => {
    const deadline = Date.now() + NEED_TOKEN_TIMEOUT_MS;
    const poll = () => {
      const token = getAccessToken();
      if (token) { resolve(token); return; }
      if (Date.now() >= deadline) { resolve(null); return; }
      setTimeout(poll, 20);
    };
    poll();
  });
}

export function refreshAccessToken() {
  if (inFlight) return inFlight;
  const tokenAtStart = getAccessToken();
  const genAtStart = readGen();

  inFlight = runExclusive(async () => {
    if (isFreshTokenFromElsewhere(tokenAtStart)) return getAccessToken();

    // Re-read the generation counter now that we hold the lock (or ran synchronously without
    // one). A change here — even without a delivered broadcast — means another tab already
    // consumed the refresh cookie while we were waiting.
    const genInsideLock = readGen();
    const otherTabAlreadyRefreshed = genAtStart !== null && genInsideLock !== null && genInsideLock !== genAtStart;

    if (otherTabAlreadyRefreshed) {
      const token = await waitForTokenFromOtherTabs();
      if (token) return token;
      // No token showed up in time: do NOT resend the already-used cookie (strict reuse would
      // revoke the whole family). Fail the refresh and let the caller treat it like a 401.
      throw new Error('refresh unavailable: another tab already used the refresh token');
    }

    const data = await refreshRequest();
    setAccessToken(data.accessToken, data.expiresAtUtc);
    if (genInsideLock !== null) writeGen(genInsideLock + 1);
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

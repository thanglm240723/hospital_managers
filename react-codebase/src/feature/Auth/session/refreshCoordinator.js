import { refresh as refreshRequest } from '../api/authClient';
import {
  getAccessToken, getExpiresAt, setAccessToken, clearAccessToken,
} from './tokenStore';

// Server dùng strict reuse (Đặc tả kỹ thuật §4.3): hai request refresh cùng một cookie ⇒ cả phiên bị thu hồi.
// Vì vậy: 1 lời gọi/tab (single-flight) và 1 lời gọi/trình duyệt (Web Locks); tab xong thì phát token cho tab khác.
const LOCK_NAME = 'auth-refresh';
const CHANNEL_NAME = 'auth';
const FRESH_MARGIN_MS = 60 * 1000;

let inFlight = null;
let channel = null;
const remoteLogoutListeners = new Set();

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

export function refreshAccessToken() {
  if (inFlight) return inFlight;
  const tokenAtStart = getAccessToken();

  inFlight = runExclusive(async () => {
    if (isFreshTokenFromElsewhere(tokenAtStart)) return getAccessToken();
    const data = await refreshRequest();
    setAccessToken(data.accessToken, data.expiresAtUtc);
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

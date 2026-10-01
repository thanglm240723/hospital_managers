import { refresh as refreshRequest } from '../api/authClient';
import {
  getAccessToken, getExpiresAt, setAccessToken, subscribe,
} from './tokenStore';
import {
  getSessionEpoch, isCurrentSessionEpoch, invalidateLocalSession, isAcceptingTokens, getSessionGeneration, isCurrentSharedSession,
} from './sessionLifecycle';

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
// requestId của lời hỏi need-token đang chờ; phản hồi mang requestId khác (hoặc đến sau logout) bị bỏ qua.
let pendingRequestId = null;
let requestSeq = 0;
const remoteLogoutListeners = new Set();

// Fix round 2: the generation (see GEN_KEY) at which THIS tab's current in-memory token was
// obtained — updated whenever the token changes for any reason (login, our own successful
// refresh, or receiving another tab's broadcast). Comparing this remembered value to a fresh
// localStorage read *inside* the lock — instead of a `genAtStart` snapshot taken when
// refreshAccessToken() was called — closes the residual window where a tab starts a refresh
// call *after* the winner already wrote the counter and released the lock, but *before* that
// tab has processed the winner's `token` broadcast: such a call would otherwise read the
// already-bumped counter as its own baseline, see no mismatch, and resend the consumed cookie.
let myTokenGen = null;

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

subscribe((current) => { myTokenGen = current ? readGen() : null; });

// Thông điệp chỉ có hiệu lực khi mang đúng thế hệ phiên dùng chung của tab này (không nhận `{type:'token'}` trần).
function isFromMySession(message) {
  const sessionGen = getSessionGeneration();
  return sessionGen !== null && message.sessionGen === sessionGen && isCurrentSharedSession();
}

// Sau logout tab không nhận token tự phát; phản hồi need-token phải khớp requestId đang chờ.
function acceptsBroadcastToken(message) {
  if (!isAcceptingTokens() || !isFromMySession(message)) return false;
  if (message.requestId) return message.requestId === pendingRequestId;
  return true;
}

function getChannel() {
  if (channel || typeof BroadcastChannel === 'undefined') return channel;
  channel = new BroadcastChannel(CHANNEL_NAME);
  channel.onmessage = (event) => {
    const message = event.data || {};
    if (message.type === 'token' && acceptsBroadcastToken(message)) setAccessToken(message.accessToken, message.expiresAtUtc);
    // Cố ý không dùng isFromMySession/isCurrentSharedSession: endSharedSession đã đổi thế hệ dùng chung trước khi phát logout nên các kiểm tra đó luôn sai.
    if (message.type === 'logout' && message.sessionGen === getSessionGeneration() && getSessionGeneration() !== null) {
      pendingRequestId = null;
      invalidateLocalSession();
      remoteLogoutListeners.forEach(listener => listener());
    }
    if (message.type === 'need-token' && isFromMySession(message)) {
      const token = getAccessToken();
      const expiresAt = getExpiresAt();
      if (token && expiresAt && expiresAt - Date.now() > FRESH_MARGIN_MS) {
        channel.postMessage({
          type: 'token', requestId: message.requestId || null, sessionGen: getSessionGeneration(), accessToken: token, expiresAtUtc: new Date(expiresAt).toISOString(),
        });
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
// updated our in-memory token store. Only accepts a token that is actually NEW and fresh
// relative to tokenAtStart — during a proactive refresh (or a 401 retry) the OLD token is still
// sitting in memory, and returning it immediately would make the caller retry against a token
// that is about to (or already did) expire, without ever rescheduling the proactive timer.
function waitForTokenFromOtherTabs(tokenAtStart) {
  const ch = getChannel();
  if (!ch) return Promise.resolve(null);
  requestSeq += 1;
  const requestId = `${Date.now()}-${requestSeq}`;
  pendingRequestId = requestId;
  ch.postMessage({ type: 'need-token', requestId, sessionGen: getSessionGeneration() });
  return new Promise((resolve) => {
    const deadline = Date.now() + NEED_TOKEN_TIMEOUT_MS;
    const finish = (value) => {
      if (pendingRequestId === requestId) pendingRequestId = null;
      resolve(value);
    };
    const poll = () => {
      if (pendingRequestId !== requestId) { finish(null); return; }
      if (isFreshTokenFromElsewhere(tokenAtStart)) { finish(getAccessToken()); return; }
      if (Date.now() >= deadline) { finish(null); return; }
      setTimeout(poll, 20);
    };
    poll();
  });
}

export function refreshAccessToken() {
  if (inFlight) return inFlight;
  const tokenAtStart = getAccessToken();

  const epoch = getSessionEpoch();
  const sessionGen = getSessionGeneration();
  // Request bắt đầu trước logout/đăng nhập tài khoản khác không được áp dụng hay phát token.
  const stillMySession = () => isCurrentSessionEpoch(epoch) && sessionGen !== null && getSessionGeneration() === sessionGen && isCurrentSharedSession();

  inFlight = runExclusive(async () => {
    // Phiên đã kết thúc/thay thế trước khi tới lượt: không dùng cookie (có thể thuộc phiên khác).
    if (!stillMySession()) throw new Error('refresh discarded: session ended');
    if (isFreshTokenFromElsewhere(tokenAtStart)) return getAccessToken();

    // Compare a fresh in-lock read of the counter to the generation OUR current token is known
    // to belong to (myTokenGen), not a snapshot taken when refreshAccessToken() was called. A
    // gen we don't yet know about (myTokenGen === null) means we have no baseline — e.g. a cold
    // tab that has never held a coordinator-tracked token — so we cannot conclude another tab
    // already refreshed and must attempt the network call ourselves.
    const genInsideLock = readGen();
    const otherTabAlreadyRefreshed = genInsideLock !== null && myTokenGen !== null && genInsideLock > myTokenGen;

    if (otherTabAlreadyRefreshed) {
      const token = await waitForTokenFromOtherTabs(tokenAtStart);
      if (token) return token;
      // No token showed up in time: do NOT resend the already-used cookie (strict reuse would
      // revoke the whole family). Fail the refresh and let the caller treat it like a 401.
      throw new Error('refresh unavailable: another tab already used the refresh token');
    }

    const data = await refreshRequest();
    // Logout (cục bộ hoặc tab khác) xảy ra trong lúc chờ: không áp dụng token đến muộn.
    if (!stillMySession()) throw new Error('refresh discarded: session ended');
    // Write the counter BEFORE setting the token: setAccessToken synchronously notifies our own
    // subscribe listener above, which re-reads the counter into myTokenGen — it must already
    // reflect this refresh's new generation by then.
    if (genInsideLock !== null) writeGen(genInsideLock + 1);
    setAccessToken(data.accessToken, data.expiresAtUtc);
    const ch = getChannel();
    if (ch) ch.postMessage({ type: 'token', sessionGen, accessToken: data.accessToken, expiresAtUtc: data.expiresAtUtc });
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

export function cancelPendingTokenRequests() {
  pendingRequestId = null;
}

// sessionGen: thế hệ phiên vừa kết thúc (lấy TRƯỚC khi huỷ phiên cục bộ).
export function broadcastLogout(sessionGen) {
  const ch = getChannel();
  if (ch) ch.postMessage({ type: 'logout', sessionGen });
}

export function onRemoteLogout(listener) {
  getChannel();
  remoteLogoutListeners.add(listener);
  return () => remoteLogoutListeners.delete(listener);
}

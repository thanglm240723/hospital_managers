import { subscribe, getExpiresAt } from './tokenStore';
import { refreshAccessToken } from './refreshCoordinator';

// Refresh chủ động trước khi access token hết hạn để người dùng đang thao tác không gặp lỗi.
const LEAD_MS = 60 * 1000;
// Fix round 1: every tab shares the same expiresAt, so unjittered timers all fire in the same
// millisecond, which is exactly the cross-tab lock/broadcast race that revokes the session
// family under strict reuse. A few seconds of random spread de-synchronises tabs so the
// generation-counter / need-token fallback in refreshCoordinator rarely has to engage.
const JITTER_MAX_MS = 5 * 1000;

let timer = null;
let unsubscribe = null;

function schedule(onFailure) {
  clearTimeout(timer);
  timer = null;
  const expiresAt = getExpiresAt();
  if (!expiresAt) return;
  const jitter = Math.floor(Math.random() * JITTER_MAX_MS);
  const delay = Math.max(expiresAt - LEAD_MS - Date.now(), 0) + jitter;
  timer = setTimeout(() => {
    refreshAccessToken().catch(onFailure);
  }, delay);
}

export function startRefreshScheduler(onFailure) {
  stopRefreshScheduler();
  unsubscribe = subscribe(() => schedule(onFailure));
  schedule(onFailure);
}

export function stopRefreshScheduler() {
  clearTimeout(timer);
  timer = null;
  if (unsubscribe) unsubscribe();
  unsubscribe = null;
}

import { subscribe, getExpiresAt } from './tokenStore';
import { refreshAccessToken } from './refreshCoordinator';

// Refresh chủ động trước khi access token hết hạn để người dùng đang thao tác không gặp lỗi.
const LEAD_MS = 60 * 1000;

let timer = null;
let unsubscribe = null;

function schedule(onFailure) {
  clearTimeout(timer);
  timer = null;
  const expiresAt = getExpiresAt();
  if (!expiresAt) return;
  const delay = Math.max(expiresAt - LEAD_MS - Date.now(), 0);
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

// Access token chỉ sống trong RAM của tab (Đặc tả kỹ thuật §4.1):
// không Redux-persist, không localStorage/sessionStorage. F5 thì lấy lại bằng refresh cookie.
let current = null;
const listeners = new Set();

export function getAccessToken() {
  return current ? current.accessToken : null;
}

export function getExpiresAt() {
  return current ? current.expiresAt : null;
}

export function setAccessToken(accessToken, expiresAtUtc) {
  current = { accessToken, expiresAt: new Date(expiresAtUtc).getTime() };
  listeners.forEach(listener => listener(current));
}

export function clearAccessToken() {
  current = null;
  listeners.forEach(listener => listener(null));
}

export function subscribe(listener) {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

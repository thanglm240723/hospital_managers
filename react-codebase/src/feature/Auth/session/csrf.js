export const CSRF_COOKIE = '__Host-csrf';

// Cookie CSRF cố ý KHÔNG HttpOnly: JS đọc rồi gửi lại trong header X-CSRF-Token.
export function readCsrfToken(cookieString = typeof document !== 'undefined' ? document.cookie : '') {
  const prefix = `${CSRF_COOKIE}=`;
  const match = cookieString
    .split(';')
    .map(part => part.trim())
    .find(part => part.indexOf(prefix) === 0);
  return match ? decodeURIComponent(match.substring(prefix.length)) : null;
}

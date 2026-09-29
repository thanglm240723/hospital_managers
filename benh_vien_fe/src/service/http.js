import axios from 'axios';
import { getAccessToken } from '../feature/Auth/session/tokenStore';
import { refreshAccessToken } from '../feature/Auth/session/refreshCoordinator';
import { readCsrfToken } from '../feature/Auth/session/csrf';

const handlers = {
  onSessionExpired: () => {},
  onPasswordChangeRequired: () => {},
};

export function setSessionHandlers(next) {
  Object.assign(handlers, next);
}

// Client dùng cho mọi API cần đăng nhập. Tự gắn Bearer + CSRF, tự refresh 1 lần khi gặp 401.
// Gửi lại request sau 401 là an toàn kể cả với command: 401 xảy ra ở bước xác thực, trước khi nghiệp vụ chạy.
export function createHttpClient(adapter) {
  const client = axios.create({ baseURL: process.env.API_URL, withCredentials: true, adapter });

  client.interceptors.request.use((config) => {
    const headers = { ...config.headers };
    const token = getAccessToken();
    if (token) headers.Authorization = `Bearer ${token}`;
    const method = (config.method || 'get').toLowerCase();
    if (method !== 'get' && method !== 'head') {
      const csrf = readCsrfToken();
      if (csrf) headers['X-CSRF-Token'] = csrf;
    }
    return { ...config, headers };
  });

  client.interceptors.response.use(undefined, async (error) => {
    const { response, config } = error;
    if (!response || !config) throw error;

    if (response.status === 401 && !config.retriedAfterRefresh) {
      try {
        await refreshAccessToken();
      } catch (refreshError) {
        handlers.onSessionExpired();
        throw error;
      }
      // axios 0.18 đã ghép baseURL vào config.url ở lần gửi đầu — bỏ baseURL để không ghép 2 lần.
      return client({ ...config, baseURL: '', retriedAfterRefresh: true });
    }

    if (response.status === 401) handlers.onSessionExpired();
    if (response.status === 403 && response.data && response.data.code === 'password_change_required') {
      handlers.onPasswordChangeRequired();
    }
    throw error;
  });

  return client;
}

const http = createHttpClient();
export default http;

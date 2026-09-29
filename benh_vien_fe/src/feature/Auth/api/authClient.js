import axios from 'axios';
import { readCsrfToken } from '../session/csrf';

// Client "trần" cho các endpoint dựa trên cookie — KHÔNG gắn interceptor refresh,
// nếu không refresh thất bại sẽ tự gọi refresh (vòng lặp).
const raw = axios.create({ baseURL: process.env.API_URL, withCredentials: true });

function csrfHeaders() {
  const token = readCsrfToken();
  return token ? { 'X-CSRF-Token': token } : {};
}

export function login(email, password) {
  return raw.post('v1/auth/login', { email, password }).then(response => response.data);
}

export function refresh() {
  return raw.post('v1/auth/refresh', null, { headers: csrfHeaders() }).then(response => response.data);
}

export function logout() {
  return raw.post('v1/auth/logout', null, { headers: csrfHeaders() });
}

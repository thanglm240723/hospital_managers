// Hợp đồng DỰ KIẾN — chưa có ở BE (Gateway đã có route /api/v1/users/{**catch-all}, chưa có Carter endpoint).
// CẦN_XÁC_NHẬN khi BE triển khai module quản lý tài khoản.
import http from 'service/http';

export const listUsers = ({
  pageNumber = 1, pageSize = 20, searchTerm = '', roleId = '', status = '',
} = {}) => http
  .get('v1/users', { params: { pageNumber, pageSize, searchTerm, roleId, status } })
  .then(response => response.data);

export const getUser = id => http.get(`v1/users/${id}`).then(response => response.data);

export const createUser = payload => http.post('v1/users', payload).then(response => response.data);

export const assignRoles = (id, roleIds) => http.post(`v1/users/${id}/roles`, { roleIds }).then(response => response.data);

export const grantPermissions = (id, permissionCodes) => http
  .post(`v1/users/${id}/permissions/grant`, { permissionCodes })
  .then(response => response.data);

export const revokePermissions = (id, permissionCodes) => http
  .post(`v1/users/${id}/permissions/revoke`, { permissionCodes })
  .then(response => response.data);

export const resetTemporaryPassword = id => http.post(`v1/users/${id}/reset-password`).then(response => response.data);

export const activateUser = (id, idempotencyKey) => http
  .post(`v1/users/${id}/activate`, null, { headers: { 'Idempotency-Key': idempotencyKey } })
  .then(response => response.data);

export const deactivateUser = (id, idempotencyKey) => http
  .post(`v1/users/${id}/deactivate`, null, { headers: { 'Idempotency-Key': idempotencyKey } })
  .then(response => response.data);

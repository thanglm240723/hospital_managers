// Hợp đồng DỰ KIẾN — chưa có ở BE (Gateway đã có route /api/v1/roles/{**catch-all}, chưa có Carter endpoint).
import http from 'service/http';

export const listRoles = () => http.get('v1/roles').then(response => response.data);
export const getRole = id => http.get(`v1/roles/${id}`).then(response => response.data);
export const createRole = payload => http.post('v1/roles', payload).then(response => response.data);
export const updateRolePermissions = (id, permissionCodes) => http
  .post(`v1/roles/${id}/permissions`, { permissionCodes })
  .then(response => response.data);

import http from 'service/http';

// Hợp đồng BE (plan 05): GET v1/roles, GET v1/permissions, POST v1/roles, PUT v1/roles/{id}, PUT v1/roles/{id}/permissions (If-Match = rowVersion).
// Giữ nguyên tên trường của server (isSystem, permissionCodes, rowVersion).
const ifMatch = version => ({ headers: { 'If-Match': String(version) } });

export const listRoles = () => http.get('v1/roles').then(response => response.data);
export const listPermissions = () => http.get('v1/permissions').then(response => response.data);
export const createRole = ({ code, name, permissionCodes }) => http
  .post('v1/roles', { code, name, permissionCodes })
  .then(response => response.data);
export const renameRole = (id, name, version) => http
  .put(`v1/roles/${id}`, { name }, ifMatch(version))
  .then(response => response.data);
export const updateRolePermissions = (id, permissionCodes, version) => http
  .put(`v1/roles/${id}/permissions`, { permissionCodes }, ifMatch(version))
  .then(response => response.data);

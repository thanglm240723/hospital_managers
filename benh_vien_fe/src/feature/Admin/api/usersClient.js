import http from 'service/http';

// Hợp đồng BE (plan 06): GET/POST v1/users, GET v1/users/{id}, POST …/activate|deactivate,
// PUT …/roles, POST …/permissions/grant|revoke (ba lệnh sau gửi If-Match = rowVersion).
// Giữ nguyên tên trường của server (isActive, mustChangePassword, roles, permissionGrants, effectivePermissions, rowVersion).
const ifMatch = version => ({ headers: { 'If-Match': String(version) } });

const compact = params => Object.keys(params).reduce((acc, key) => (
  params[key] === '' || params[key] === undefined || params[key] === null ? acc : { ...acc, [key]: params[key] }
), {});

export const listUsers = ({
  pageNumber = 1, pageSize = 10, searchTerm = '', roleId = '', status = '',
} = {}) => http
  .get('v1/users', { params: compact({ pageNumber, pageSize, searchTerm, roleId, status }) })
  .then(response => response.data);

export const getUser = id => http.get(`v1/users/${id}`).then(response => response.data);

export const createUser = ({ email, fullName, roleIds }) => http
  .post('v1/users', { email, fullName, roleIds })
  .then(response => response.data);

export const assignRoles = (id, roleIds, version) => http
  .put(`v1/users/${id}/roles`, { roleIds }, ifMatch(version))
  .then(response => response.data);

export const grantPermission = (id, permissionCode, reason, version) => http
  .post(`v1/users/${id}/permissions/grant`, { permissionCode, reason }, ifMatch(version))
  .then(response => response.data);

export const revokePermission = (id, permissionCode, reason, version) => http
  .post(`v1/users/${id}/permissions/revoke`, { permissionCode, reason }, ifMatch(version))
  .then(response => response.data);

export const activateUser = id => http.post(`v1/users/${id}/activate`).then(response => response.data);

export const deactivateUser = id => http.post(`v1/users/${id}/deactivate`).then(response => response.data);

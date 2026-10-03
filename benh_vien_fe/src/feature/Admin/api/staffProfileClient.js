import http from 'service/http';

// Hợp đồng BE (plan 01 Task 5): PUT không If-Match = tạo (201), có If-Match = cập nhật (200); work-scopes bắt buộc If-Match.
export const getStaffProfile = userId => http.get(`v1/users/${userId}/staff-profile`).then(response => response.data);
export const createStaffProfile = (userId, { staffCode, isActive }) => http
  .put(`v1/users/${userId}/staff-profile`, { staffCode, isActive })
  .then(response => response.data);
export const updateStaffProfile = (userId, { staffCode, isActive }, rowVersion) => http
  .put(`v1/users/${userId}/staff-profile`, { staffCode, isActive }, { headers: { 'If-Match': String(rowVersion) } })
  .then(response => response.data);
export const setWorkScopes = (userId, departmentIds, rowVersion) => http
  .put(`v1/users/${userId}/staff-profile/work-scopes`, { departmentIds }, { headers: { 'If-Match': String(rowVersion) } })
  .then(response => response.data);

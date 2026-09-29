// Mã quyền dùng trong UI để ẩn/hiện — quyết định thật luôn ở backend.
// Nguồn: benh_vien_be/src/QuanLyBenhVien.Domain/Identity/Permissions.cs (đã triển khai).
export const PERMISSIONS = {
  USERS_READ: 'users.read',
  USERS_CREATE: 'users.create',
  USERS_ACTIVATE: 'users.activate',
  USERS_ROLES_MANAGE: 'users.roles.manage',
  USERS_PERMISSIONS_MANAGE: 'users.permissions.manage',
  ROLES_READ: 'roles.read',
  ROLES_MANAGE: 'roles.manage',
  PERMISSIONS_READ: 'permissions.read',

  // DỰ_KIẾN — chưa có ở BE, CẦN_XÁC_NHẬN. Đặt tên theo quy ước <module>.<hành động> của Permissions.cs hiện có.
  PATIENTS_READ: 'patients.read', // DỰ_KIẾN — chưa có ở BE, CẦN_XÁC_NHẬN
  PATIENTS_CREATE: 'patients.create', // DỰ_KIẾN — chưa có ở BE, CẦN_XÁC_NHẬN
  RECEPTION_REGISTER: 'reception.register', // DỰ_KIẾN — chưa có ở BE, CẦN_XÁC_NHẬN
  QUEUES_CALL: 'queues.call', // DỰ_KIẾN — chưa có ở BE, CẦN_XÁC_NHẬN
  VITALS_RECORD: 'vitals.record', // DỰ_KIẾN — chưa có ở BE, CẦN_XÁC_NHẬN
  ENCOUNTERS_EXAMINE: 'encounters.examine', // DỰ_KIẾN — chưa có ở BE, CẦN_XÁC_NHẬN
};

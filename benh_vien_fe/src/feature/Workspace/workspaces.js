import { PERMISSIONS } from 'feature/Auth/permissionCodes';

// Khu vực làm việc = nhóm màn hình theo vai trò. Route mặc định của mỗi khu vực là màn đầu tiên người dùng thấy.
export const WORKSPACES = [
  {
    id: 'admin',
    name: 'Quản trị hệ thống',
    description: 'Quản lý tài khoản, vai trò và phân quyền.',
    permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.ROLES_READ],
    defaultRoute: '/admin/users',
  },
  {
    id: 'reception',
    name: 'Tiếp nhận',
    description: 'Tìm hồ sơ bệnh nhân, tiếp nhận và cấp số khám.',
    permissions: [PERMISSIONS.PATIENTS_READ],
    defaultRoute: '/reception/patients',
  },
  {
    id: 'clinic',
    name: 'Khám ngoại trú',
    description: 'Hàng chờ khám, phiếu khám, chỉ định và đơn thuốc.',
    permissions: [PERMISSIONS.ENCOUNTERS_EXAMINE],
    defaultRoute: '/clinic/queue',
  },
  {
    id: 'vitals',
    name: 'Sinh hiệu',
    description: 'Đo và ghi nhận sinh hiệu trước khi khám.',
    permissions: [PERMISSIONS.VITALS_RECORD],
    defaultRoute: '/vitals',
  },
];

// Khu vực mà người dùng có ít nhất một quyền trong danh sách permissions.
export const workspacesForUser = permissions => WORKSPACES.filter(
  workspace => workspace.permissions.some(code => permissions && permissions.indexOf(code) !== -1),
);

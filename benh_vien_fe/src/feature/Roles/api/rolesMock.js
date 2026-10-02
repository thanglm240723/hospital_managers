import { mockDelay, mockReject } from 'service/mockMode';
import { nextMockId } from 'service/mockData';
import { SYSTEM_ROLES, MOCK_PERMISSIONS } from '../permissionCatalog';

// Mock chỉ bật khi REACT_APP_USE_MOCK_API=true; cùng hình dạng với BE thật.
let roles = SYSTEM_ROLES.map((role, index) => ({
  id: role.id,
  code: role.id.replace('role-', ''),
  name: role.name,
  isSystem: true,
  permissionCodes: MOCK_PERMISSIONS.filter((p, i) => i % SYSTEM_ROLES.length === index || i < 2).map(p => p.code),
  rowVersion: 1,
}));

export const listRoles = () => mockDelay(roles);
export const listPermissions = () => mockDelay(MOCK_PERMISSIONS);

export function createRole({ code, name, permissionCodes }) {
  if (roles.some(r => r.code === code)) return mockReject({ status: 409, code: 'role_code_taken', title: 'Mã vai trò đã tồn tại.' });
  const role = {
    id: nextMockId('role'), code, name, isSystem: false, permissionCodes: permissionCodes || [], rowVersion: 1,
  };
  roles = [...roles, role];
  return mockDelay(role);
}

function change(id, version, patch) {
  const current = roles.find(r => r.id === id);
  if (!current) return mockReject({ status: 404, code: 'role_not_found', title: 'Không tìm thấy vai trò.' });
  if (current.rowVersion !== Number(version)) {
    return mockReject({ status: 412, code: 'role_version_conflict', title: 'Vai trò đã bị thay đổi. Vui lòng nạp lại.' });
  }
  const next = { ...current, ...patch, rowVersion: current.rowVersion + 1 };
  roles = roles.map(r => (r.id === id ? next : r));
  return mockDelay(next);
}

export const renameRole = (id, name, version) => change(id, version, { name });
export const updateRolePermissions = (id, permissionCodes, version) => change(id, version, { permissionCodes });

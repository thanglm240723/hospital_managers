import { mockDelay } from 'service/mockMode';
import { nextMockId } from 'service/mockData';
import { SYSTEM_ROLES, PERMISSION_CATALOG } from '../permissionCatalog';

const ALL_CODES = PERMISSION_CATALOG.flatMap(group => group.items.map(item => item.code));

let customRoles = [];
const systemPermissions = {};
SYSTEM_ROLES.forEach((role, index) => {
  // Dữ liệu giả: mỗi vai trò hệ thống có một tập quyền phù hợp module tương ứng theo thứ tự khai báo.
  systemPermissions[role.id] = ALL_CODES.filter((code, i) => i % SYSTEM_ROLES.length === index || i < 2);
});

function toDto(role) {
  const permissions = role.system ? systemPermissions[role.id] : role.permissions;
  return { ...role, permissions: permissions || [] };
}

export function listRoles() {
  const all = [...SYSTEM_ROLES, ...customRoles].map(toDto);
  return mockDelay(all);
}

export function getRole(id) {
  const role = [...SYSTEM_ROLES, ...customRoles].find(r => r.id === id);
  return mockDelay(role ? toDto(role) : null);
}

export function createRole(payload) {
  const role = {
    id: nextMockId('role'), name: payload.name, system: false, permissions: payload.permissions || [],
  };
  customRoles = [...customRoles, role];
  return mockDelay(toDto(role));
}

export function updateRolePermissions(id, permissionCodes) {
  customRoles = customRoles.map(r => (r.id === id ? { ...r, permissions: permissionCodes } : r));
  const role = customRoles.find(r => r.id === id);
  return mockDelay(toDto(role));
}

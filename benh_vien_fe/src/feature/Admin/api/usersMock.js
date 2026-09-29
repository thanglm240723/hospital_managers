import { mockDelay, mockReject } from 'service/mockMode';
import { FAKE_NAMES, nextMockId } from 'service/mockData';
import { SYSTEM_ROLES } from 'feature/Roles/permissionCatalog';

const STATUSES = ['active', 'must_change_password', 'locked'];

let users = FAKE_NAMES.map((name, index) => ({
  id: `user-${index === 0 ? 'admin01' : index}`,
  fullName: name,
  email: index === 0 ? 'admin01@benhvien.vn' : `nhanvien${index}@benhvien.vn`,
  status: STATUSES[index % STATUSES.length],
  roleIds: [SYSTEM_ROLES[index % SYSTEM_ROLES.length].id],
  extraPermissions: [],
  revokedPermissions: [],
  isSeedAdmin: index === 0,
}));

function findUser(id) {
  const user = users.find(u => u.id === id);
  if (!user) throw { response: { status: 404, data: { title: 'Không tìm thấy tài khoản.', code: 'user_not_found' }, headers: {} } }; // eslint-disable-line no-throw-literal
  return user;
}

function toDto(user) {
  const roleNames = user.roleIds.map(id => (SYSTEM_ROLES.find(r => r.id === id) || {}).name).filter(Boolean);
  return { ...user, roleNames };
}

export function listUsers({
  pageNumber = 1, pageSize = 20, searchTerm = '', roleId = '', status = '',
} = {}) {
  let items = users;
  if (searchTerm) {
    const term = searchTerm.trim().toLowerCase();
    items = items.filter(u => u.fullName.toLowerCase().includes(term) || u.email.toLowerCase().includes(term));
  }
  if (roleId) items = items.filter(u => u.roleIds.indexOf(roleId) !== -1);
  if (status) items = items.filter(u => u.status === status);

  const totalCount = items.length;
  const start = (pageNumber - 1) * pageSize;
  const pageItems = items.slice(start, start + pageSize).map(toDto);
  return mockDelay({
    items: pageItems, pageNumber, pageSize, totalCount, totalPages: Math.max(1, Math.ceil(totalCount / pageSize)),
  });
}

export function getUser(id) {
  try {
    return mockDelay(toDto(findUser(id)));
  } catch (error) {
    return Promise.reject(error);
  }
}

export function createUser(payload) {
  const user = {
    id: nextMockId('user'),
    fullName: payload.fullName,
    email: payload.email,
    status: 'must_change_password',
    roleIds: payload.roleIds || [],
    extraPermissions: [],
    revokedPermissions: [],
    isSeedAdmin: false,
  };
  users = [user, ...users];
  return mockDelay({ ...toDto(user), temporaryPassword: 'Tam#2026Doi!' });
}

export function assignRoles(id, roleIds) {
  const user = findUser(id);
  user.roleIds = roleIds;
  return mockDelay(toDto(user));
}

export function grantPermissions(id, permissionCodes) {
  const user = findUser(id);
  user.extraPermissions = Array.from(new Set([...user.extraPermissions, ...permissionCodes]));
  user.revokedPermissions = user.revokedPermissions.filter(code => permissionCodes.indexOf(code) === -1);
  return mockDelay(toDto(user));
}

export function revokePermissions(id, permissionCodes) {
  const user = findUser(id);
  user.revokedPermissions = Array.from(new Set([...user.revokedPermissions, ...permissionCodes]));
  user.extraPermissions = user.extraPermissions.filter(code => permissionCodes.indexOf(code) === -1);
  return mockDelay(toDto(user));
}

export function resetTemporaryPassword(id) {
  const user = findUser(id);
  user.status = 'must_change_password';
  return mockDelay({ ...toDto(user), temporaryPassword: 'Tam#2026Doi!' });
}

export function activateUser(id) {
  const user = findUser(id);
  user.status = 'active';
  return mockDelay(toDto(user));
}

export function deactivateUser(id) {
  const user = findUser(id);
  if (user.isSeedAdmin) {
    return mockReject({
      title: 'Không thể khóa quản trị viên đang hoạt động cuối cùng.',
      code: 'last_active_admin',
      status: 409,
    });
  }
  user.status = 'locked';
  return mockDelay(toDto(user));
}

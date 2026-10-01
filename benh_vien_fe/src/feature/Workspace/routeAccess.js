import { matchPath } from 'react-router-dom';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { WORKSPACES } from './workspaces';

// Route registry: nguồn chung cho route guard, sidebar, default route và return URL.
// permission = quyền cần (từ GET /api/v1/auth/me); availability = khóa phát hành tính năng (không thay quyền).
// label có ⇒ hiện trong menu của khu vực; thứ tự quyết định route mặc định.
export const ROUTES = [
  { path: '/admin/users', workspaceId: 'admin', permission: PERMISSIONS.USERS_READ, availability: 'admin.users', label: 'Tài khoản' },
  { path: '/admin/roles', workspaceId: 'admin', permission: PERMISSIONS.ROLES_READ, availability: 'admin.roles', label: 'Vai trò & quyền' },
  {
    path: '/reception/patients',
    workspaceId: 'reception',
    permission: PERMISSIONS.PATIENTS_READ,
    availability: 'reception.patients',
    label: 'Tìm hồ sơ',
  },
  { path: '/reception/intake/:patientId', workspaceId: 'reception', permission: PERMISSIONS.PATIENTS_READ, availability: 'reception.intake' },
  { path: '/clinic/queue', workspaceId: 'clinic', permission: PERMISSIONS.ENCOUNTERS_EXAMINE, availability: 'clinic.queue', label: 'Hàng chờ khám' },
  { path: '/clinic/encounters/:id', workspaceId: 'clinic', permission: PERMISSIONS.ENCOUNTERS_EXAMINE, availability: 'clinic.encounter' },
  { path: '/vitals', workspaceId: 'vitals', permission: PERMISSIONS.VITALS_RECORD, availability: 'vitals.queue', label: 'Sinh hiệu' },
];

const has = (permissions, code) => Array.isArray(permissions) && permissions.indexOf(code) !== -1;
const isReleased = (availability, key) => Boolean(availability && availability[key]);

// Nhận cả URL thật (/clinic/encounters/123) lẫn pattern (/clinic/encounters/:id).
export const findRoute = path => (typeof path === 'string' ? ROUTES.find(r => matchPath(path, { path: r.path, exact: true })) || null : null);

// 'ok' | 'unavailable' (có quyền, màn chưa phát hành) | 'forbidden' | 'unknown' (không thuộc registry).
export const routeStatus = (path, permissions, availability) => {
  const route = findRoute(path);
  if (!route) return 'unknown';
  if (!has(permissions, route.permission)) return 'forbidden';
  return isReleased(availability, route.availability) ? 'ok' : 'unavailable';
};

export const canAccessRoute = (path, permissions, availability) => routeStatus(path, permissions, availability) === 'ok';

// Khu vực có ít nhất một route được phép. ready = có ít nhất một màn đã phát hành.
export const getAvailableWorkspaces = (permissions, availability) => WORKSPACES.map(workspace => {
  const routes = ROUTES.filter(r => r.workspaceId === workspace.id && has(permissions, r.permission));
  return routes.length ? { ...workspace, ready: routes.some(r => isReleased(availability, r.availability)) } : null;
}).filter(Boolean);

// Menu của MỘT khu vực (khu vực đang chọn), chỉ mục người dùng có quyền.
export const getMenuRoutes = (workspaceId, permissions, availability) => ROUTES.filter(
  r => r.label && r.workspaceId === workspaceId && has(permissions, r.permission),
).map(r => ({ ...r, ready: isReleased(availability, r.availability) }));

// Ưu tiên màn đã phát hành; nếu chưa có màn nào thì vào màn có quyền đầu tiên (hiện thông báo đang hoàn thiện).
export const getDefaultRoute = (workspaceId, permissions, availability) => {
  const menu = getMenuRoutes(workspaceId, permissions, availability);
  const target = menu.find(r => r.ready) || menu[0];
  return target ? target.path : null;
};

export const isWorkspaceAllowed = (workspaceId, permissions, availability) => getAvailableWorkspaces(permissions, availability)
  .some(w => w.id === workspaceId);

// Return URL chỉ dùng khi là route nội bộ của registry, thuộc đúng khu vực đã chọn và người dùng có quyền.
export const resolveStartTarget = (workspaceId, from, permissions, availability) => {
  const pathname = from && typeof from.pathname === 'string' ? from.pathname : null;
  const route = pathname && pathname.charAt(0) === '/' && pathname.charAt(1) !== '/' ? findRoute(pathname) : null;
  if (route && route.workspaceId === workspaceId && has(permissions, route.permission)) {
    return { pathname, search: from.search || '' };
  }
  return getDefaultRoute(workspaceId, permissions, availability);
};

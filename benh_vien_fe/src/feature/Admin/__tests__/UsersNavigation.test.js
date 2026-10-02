import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { FEATURE_AVAILABILITY } from 'feature/Workspace/availability';
import { routeStatus, getDefaultRoute } from 'feature/Workspace/routeAccess';

// Dùng availability thật (không mock) để chứng minh màn Tài khoản đã được bật.
describe('Màn Tài khoản trong route registry', () => {
  it('admin.users đã phát hành', () => {
    expect(FEATURE_AVAILABILITY['admin.users']).toBe(true);
  });

  it('có users.read → ok; thiếu quyền → forbidden', () => {
    expect(routeStatus('/admin/users', [PERMISSIONS.USERS_READ], FEATURE_AVAILABILITY)).toBe('ok');
    expect(routeStatus('/admin/users', [PERMISSIONS.ROLES_READ], FEATURE_AVAILABILITY)).toBe('forbidden');
  });

  it('mặc định khu vực admin là /admin/users khi có users.read', () => {
    expect(getDefaultRoute('admin', [PERMISSIONS.USERS_READ, PERMISSIONS.ROLES_READ], FEATURE_AVAILABILITY)).toBe('/admin/users');
  });
});

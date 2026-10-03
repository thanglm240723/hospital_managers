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

describe('Chế độ mock', () => {
  it('màn Tài khoản không hỗ trợ mock: availability admin.users tắt', () => {
    const old = process.env.REACT_APP_USE_MOCK_API;
    process.env.REACT_APP_USE_MOCK_API = 'true';
    jest.resetModules();
    const mocked = require('feature/Workspace/availability').FEATURE_AVAILABILITY; // eslint-disable-line global-require
    process.env.REACT_APP_USE_MOCK_API = old;
    expect(mocked['admin.users']).toBe(false);
    expect(mocked['admin.roles']).toBe(true);
  });
});

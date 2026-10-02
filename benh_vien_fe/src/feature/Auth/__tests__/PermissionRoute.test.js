import { act } from 'react-dom/test-utils';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { AUTH_ANONYMOUS } from 'feature/Auth/redux/actionTypes';
import { mountApp, pageMounts, click } from 'testUtils/appHarness';

jest.mock('service/http', () => ({ get: jest.fn(), post: jest.fn() }));

let app;
afterEach(() => {
  if (app) app.unmount();
  app = null;
});

const cards = () => app.container.querySelectorAll('.c-workspace-picker__card');

describe('PermissionRoute — availability thật (admin.roles đã bật ở plan 05, admin.users còn tắt)', () => {
  it('chỉ users.read: vào /admin/users, hiện "đang hoàn thiện", không báo thiếu quyền, không mount màn gọi API', () => {
    app = mountApp({ permissions: [PERMISSIONS.USERS_READ] });
    expect(app.history.location.pathname).toBe('/admin/users');
    expect(app.container.textContent).toContain('Khu vực đang hoàn thiện');
    expect(app.container.textContent).not.toContain('chưa được cấp quyền');
    expect(pageMounts).toEqual([]);
  });
});

describe('PermissionRoute — guard', () => {
  it('URL trực tiếp không đủ quyền: không mount màn, về màn có quyền', () => {
    app = mountApp({ path: '/admin/users', permissions: [PERMISSIONS.ROLES_READ] });
    expect(pageMounts).not.toContain('users');
    expect(app.history.location.pathname).toBe('/admin/roles');
  });

  it('chưa đăng nhập → /login giữ from', () => {
    app = mountApp({ path: '/admin/roles' });
    act(() => {
      app.store.dispatch({ type: AUTH_ANONYMOUS });
    });
    expect(app.history.location.pathname).toBe('/login');
    expect(app.history.location.state.from.pathname).toBe('/admin/roles');
  });

  it('bắt đổi mật khẩu đi trước chọn khu vực và màn đích', () => {
    app = mountApp({ path: '/admin/roles', permissions: [PERMISSIONS.ROLES_READ, PERMISSIONS.USERS_READ], extra: { mustChangePassword: true } });
    expect(app.history.location.pathname).toBe('/change-password');
    expect(pageMounts).toEqual([]);
  });

  it('nhiều khu vực + return URL khu vực khác: không bỏ qua picker, bỏ return URL không thuộc khu vực đã chọn', () => {
    app = mountApp({ path: '/vitals', permissions: [PERMISSIONS.ROLES_READ, PERMISSIONS.VITALS_RECORD] });
    expect(app.history.location.pathname).toBe('/start');
    expect(cards()).toHaveLength(2);
    click(cards()[0]);
    expect(app.history.location.pathname).toBe('/admin/roles');
  });

  it('return URL không thuộc registry (ngoài) bị bỏ qua', () => {
    app = mountApp({ permissions: [PERMISSIONS.VITALS_RECORD] });
    act(() => {
      app.history.push({ pathname: '/start', state: { from: { pathname: '//evil.example/x' } } });
    });
    expect(app.history.location.pathname).toBe('/vitals');
  });
});

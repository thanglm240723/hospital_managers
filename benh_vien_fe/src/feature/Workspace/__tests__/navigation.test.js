import { act } from 'react-dom/test-utils';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import http from 'service/http';
import { loadMe } from 'feature/Auth/redux/actions';
import {
  mountApp, click, change, me,
} from 'testUtils/appHarness';

jest.mock('service/http', () => ({ get: jest.fn(), post: jest.fn() }));
// Fixture: bật availability để chứng minh định tuyến (mốc 04 thật vẫn tắt — xem availability.js).
jest.mock('feature/Workspace/availability', () => ({
  FEATURE_AVAILABILITY: {
    'admin.users': true,
    'admin.roles': true,
    'reception.patients': true,
    'reception.intake': true,
    'clinic.queue': true,
    'clinic.encounter': true,
    'vitals.queue': true,
  },
}));

let app;
afterEach(() => {
  if (app) app.unmount();
  app = null;
  jest.clearAllMocks();
});

const cards = () => app.container.querySelectorAll('.c-workspace-picker__card');
const menuLinks = () => Array.from(app.container.querySelectorAll('.c-app-layout__nav-link')).map(a => a.getAttribute('href'));

describe('0/1/n khu vực', () => {
  it('0 khu vực → /no-access', () => {
    app = mountApp({ permissions: [] });
    expect(app.history.location.pathname).toBe('/no-access');
    expect(app.container.textContent).toContain('Tài khoản chưa được cấp quyền');
  });

  it('1 khu vực → vào thẳng màn mặc định', () => {
    app = mountApp({ permissions: [PERMISSIONS.PATIENTS_READ] });
    expect(app.history.location.pathname).toBe('/reception/patients');
    expect(app.screen()).toBe('patients');
    expect(app.store.getState().workspace.selectedId).toBe('reception');
  });

  it('chỉ roles.read → mặc định /admin/roles, không vào users', () => {
    app = mountApp({ permissions: [PERMISSIONS.ROLES_READ] });
    expect(app.history.location.pathname).toBe('/admin/roles');
    expect(app.screen()).toBe('roles');
    expect(menuLinks()).toEqual(['/admin/roles']);
  });

  it('nhiều khu vực → picker; bấm thẻ thật sự đổi URL và nội dung', () => {
    app = mountApp({ permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.VITALS_RECORD] });
    expect(app.history.location.pathname).toBe('/start');
    expect(cards()).toHaveLength(2);
    click(cards()[1]);
    expect(app.history.location.pathname).toBe('/vitals');
    expect(app.screen()).toBe('vitals');
  });

  it('dropdown sidebar đổi khu vực → đổi URL; menu chỉ của khu vực đang chọn', () => {
    app = mountApp({ permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.ROLES_READ, PERMISSIONS.VITALS_RECORD] });
    click(cards()[0]);
    expect(app.history.location.pathname).toBe('/admin/users');
    expect(menuLinks()).toEqual(['/admin/users', '/admin/roles']);

    change(app.container.querySelector('#workspace-select'), 'vitals');
    expect(app.history.location.pathname).toBe('/vitals');
    expect(app.screen()).toBe('vitals');
    expect(menuLinks()).toEqual(['/vitals']);
  });
});

describe('URL trực tiếp giữa các khu vực', () => {
  it('đã chọn khu vực admin, mở URL của khu vực khác có quyền → chuyển khu vực, menu chỉ của khu vực đó', () => {
    app = mountApp({ permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.ROLES_READ, PERMISSIONS.VITALS_RECORD] });
    click(cards()[0]);
    expect(app.store.getState().workspace.selectedId).toBe('admin');
    expect(menuLinks()).toEqual(['/admin/users', '/admin/roles']);

    act(() => {
      app.history.push('/vitals');
    });
    expect(app.history.location.pathname).toBe('/vitals');
    expect(app.screen()).toBe('vitals');
    expect(app.store.getState().workspace.selectedId).toBe('vitals');
    expect(menuLinks()).toEqual(['/vitals']);
  });

  it('vừa đăng nhập (chưa chọn khu vực), nhiều khu vực, mở URL trực tiếp → về picker, không vào màn', () => {
    app = mountApp({ path: '/vitals', permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.VITALS_RECORD] });
    expect(app.store.getState().workspace.selectedId).toBeNull();
    expect(app.history.location.pathname).toBe('/start');
    expect(cards()).toHaveLength(2);
    expect(app.screen()).not.toBe('vitals');
  });
});

describe('no-access, thu hồi và refresh', () => {
  it('no-access: Kiểm tra lại thành công → thoát màn cũ, vào màn đích', async () => {
    app = mountApp({ permissions: [] });
    http.get.mockResolvedValue({ data: { id: 'u1', permissions: [PERMISSIONS.VITALS_RECORD], mustChangePassword: false } });
    const button = Array.from(app.container.querySelectorAll('button')).find(b => b.textContent === 'Kiểm tra lại');
    await act(async () => {
      button.dispatchEvent(new MouseEvent('click', { bubbles: true }));
      await Promise.resolve();
      await Promise.resolve();
    });
    expect(app.history.location.pathname).toBe('/vitals');
    expect(app.screen()).toBe('vitals');
  });

  it('loadMe mới thu hồi khu vực hiện tại, còn nhiều khu vực → chọn lại', async () => {
    app = mountApp({ permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.VITALS_RECORD, PERMISSIONS.PATIENTS_READ] });
    click(cards()[0]);
    expect(app.history.location.pathname).toBe('/admin/users');

    http.get.mockResolvedValue({ data: { id: 'u1', permissions: [PERMISSIONS.VITALS_RECORD, PERMISSIONS.PATIENTS_READ], mustChangePassword: false } });
    await act(async () => {
      await app.store.dispatch(loadMe());
    });
    expect(app.history.location.pathname).toBe('/start');
    expect(app.store.getState().workspace.selectedId).toBeNull();
    expect(cards()).toHaveLength(2);
  });

  it('thu hồi hết quyền → /no-access, không loop', () => {
    app = mountApp({ permissions: [PERMISSIONS.VITALS_RECORD] });
    expect(app.history.location.pathname).toBe('/vitals');
    act(() => {
      app.store.dispatch(me([]));
    });
    expect(app.history.location.pathname).toBe('/no-access');
    expect(app.store.getState().workspace.selectedId).toBeNull();
  });

  it('refresh trong phiên (/me cùng quyền) giữ lựa chọn trong RAM', () => {
    app = mountApp({ permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.VITALS_RECORD] });
    click(cards()[1]);
    act(() => {
      app.store.dispatch(me([PERMISSIONS.USERS_READ, PERMISSIONS.VITALS_RECORD]));
    });
    expect(app.history.location.pathname).toBe('/vitals');
    expect(app.store.getState().workspace.selectedId).toBe('vitals');
  });

  it('F5 (mất RAM) với nhiều khu vực → tính lại, hiện picker, return URL hợp lệ được dùng sau khi chọn', () => {
    app = mountApp({ path: '/reception/intake/p1', permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.PATIENTS_READ] });
    expect(app.history.location.pathname).toBe('/start');
    expect(cards()).toHaveLength(2);
    click(cards()[1]);
    expect(app.history.location.pathname).toBe('/reception/intake/p1');
    expect(app.screen()).toBe('intake');
  });
});

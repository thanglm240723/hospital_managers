import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware, combineReducers } from 'redux';
import thunk from 'redux-thunk';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import adminReducer from '../redux/reducer';
import * as api from '../api';
import AdminUsersContainer from '../Container';

/* eslint-disable react/prop-types */
jest.mock('feature/Shell', () => ({
  AppLayout: ({ children }) => <div>{children}</div>,
  ConfirmDialog: () => <div />,
}));
/* eslint-enable react/prop-types */
jest.mock('../api', () => ({
  listUsers: jest.fn(),
  getUser: jest.fn(),
  createUser: jest.fn(),
  assignRoles: jest.fn(),
  grantPermission: jest.fn(),
  revokePermission: jest.fn(),
  activateUser: jest.fn(),
  deactivateUser: jest.fn(),
}));
jest.mock('feature/Roles/api', () => ({
  listRoles: jest.fn().mockResolvedValue([{ id: 'r1', code: 'doctor', name: 'Bác sĩ', permissionCodes: [] }]),
  listPermissions: jest.fn().mockResolvedValue([{ code: 'a.read', group: 'G', description: 'Xem A' }]),
}));

const summary = (id, extra = {}) => ({
  id, email: `${id}@x.vn`, fullName: `Tên ${id}`, isActive: true, mustChangePassword: false, roles: [], rowVersion: 1, ...extra,
});
const page = (items, pageNumber = 1, totalPages = 2) => ({
  items, pageNumber, pageSize: 10, totalCount: 12, totalPages,
});
const detail = (id, extra = {}) => ({ ...summary(id), permissionGrants: [], effectivePermissions: [], ...extra });

const flush = () => act(() => Promise.resolve());
const click = el => act(() => { el.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
const setInput = (el, value) => {
  const proto = el.tagName === 'SELECT' ? window.HTMLSelectElement.prototype : window.HTMLInputElement.prototype;
  Object.getOwnPropertyDescriptor(proto, 'value').set.call(el, value);
  el.dispatchEvent(new Event(el.tagName === 'SELECT' ? 'change' : 'input', { bubbles: true }));
};
const byText = (c, t) => Array.from(c.querySelectorAll('button')).find(b => b.textContent.indexOf(t) !== -1);
const deferred = () => {
  let resolve;
  let reject;
  const promise = new Promise((res, rej) => { resolve = res; reject = rej; });
  return { promise, resolve, reject };
};

let container;
async function mount(permissions = [PERMISSIONS.USERS_READ, PERMISSIONS.ROLES_READ, PERMISSIONS.PERMISSIONS_READ, PERMISSIONS.USERS_CREATE]) {
  const store = createStore(
    combineReducers({ admin: adminReducer, auth: (s = { permissions }) => s }),
    applyMiddleware(thunk),
  );
  container = document.createElement('div');
  document.body.appendChild(container);
  act(() => { ReactDOM.render(<Provider store={store}><AdminUsersContainer /></Provider>, container); });
  await flush();
  return store;
}

afterEach(() => { ReactDOM.unmountComponentAtNode(container); container.remove(); });

describe('Admin Users Container', () => {
  beforeEach(() => {
    Object.keys(api).filter(k => typeof api[k].mockReset === 'function').forEach(k => api[k].mockReset());
    api.listUsers.mockResolvedValue(page([summary('u1'), summary('u2')]));
  });

  it('tải trang 1, chuyển sang trang 2 giữ pageSize và bộ lọc', async () => {
    await mount();
    expect(api.listUsers).toHaveBeenLastCalledWith({
      searchTerm: '', roleId: '', status: '', pageNumber: 1, pageSize: 10,
    });
    api.listUsers.mockResolvedValue(page([summary('u3')], 2));
    click(byText(container, 'Trang sau'));
    await flush();
    expect(api.listUsers).toHaveBeenLastCalledWith(expect.objectContaining({ pageNumber: 2, pageSize: 10 }));
    expect(container.textContent).toContain('Tên u3');
    expect(container.textContent).toContain('Trang 2 / 2');
    expect(byText(container, 'Trang sau').disabled).toBe(true);
  });

  it('đổi bộ lọc reset về trang 1', async () => {
    await mount();
    api.listUsers.mockResolvedValue(page([summary('u3')], 2));
    click(byText(container, 'Trang sau'));
    await flush();
    act(() => setInput(container.querySelector('select[aria-label="Lọc theo trạng thái"]'), 'locked'));
    await flush();
    expect(api.listUsers).toHaveBeenLastCalledWith(expect.objectContaining({ status: 'locked', pageNumber: 1 }));
  });

  it('response tìm kiếm cũ bị bỏ (requestId)', async () => {
    await mount();
    const slow = deferred();
    const fast = deferred();
    api.listUsers.mockReset().mockReturnValueOnce(slow.promise).mockReturnValueOnce(fast.promise);
    const search = container.querySelector('input[type="search"]');
    act(() => setInput(search, 'a'));
    act(() => setInput(search, 'an'));
    fast.resolve(page([summary('moi')], 1, 1));
    await flush();
    slow.resolve(page([summary('cu')], 1, 1));
    await flush();
    expect(container.textContent).toContain('Tên moi');
    expect(container.textContent).not.toContain('Tên cu');
  });

  it('lỗi tải danh sách hiện thông báo và Thử lại nạp lại được', async () => {
    api.listUsers.mockReset().mockRejectedValueOnce(new Error('net')).mockResolvedValueOnce(page([summary('u1')], 1, 1));
    await mount();
    expect(container.textContent).toContain('Không tải được danh sách tài khoản');
    click(byText(container, 'Thử lại'));
    await flush();
    expect(container.textContent).toContain('Tên u1');
    expect(container.textContent).not.toContain('Không tải được danh sách tài khoản');
  });

  it('mở chi tiết bằng GET v1/users/{id}, không dựng panel từ summary', async () => {
    const d = deferred();
    api.getUser.mockReturnValue(d.promise);
    await mount();
    click(container.querySelectorAll('tbody tr')[0]);
    await flush();
    expect(api.getUser).toHaveBeenCalledWith('u1');
    expect(container.querySelector('.c-detail-panel')).toBeNull();
    d.resolve(detail('u1'));
    await flush();
    expect(container.querySelector('.c-detail-panel')).not.toBeNull();
  });

  it('chi tiết của user chọn trước (trả về muộn) không đè panel của user mới', async () => {
    const first = deferred();
    const second = deferred();
    api.getUser.mockReturnValueOnce(first.promise).mockReturnValueOnce(second.promise);
    await mount();
    const rows = container.querySelectorAll('tbody tr');
    click(rows[0]);
    click(rows[1]);
    second.resolve(detail('u2'));
    await flush();
    first.resolve(detail('u1'));
    await flush();
    expect(container.querySelector('.c-detail-panel__title').textContent).toBe('Tên u2');
  });

  it('lỗi tải chi tiết hiện lý do và Thử lại', async () => {
    api.getUser.mockRejectedValueOnce(new Error('net')).mockResolvedValueOnce(detail('u1'));
    await mount();
    click(container.querySelectorAll('tbody tr')[0]);
    await flush();
    expect(container.textContent).toContain('Không tải được chi tiết tài khoản');
    click(byText(container, 'Thử lại'));
    await flush();
    expect(container.querySelector('.c-detail-panel')).not.toBeNull();
  });

  it('lưu đang chờ khi đổi sang user khác: kết quả cũ không đổi panel user mới', async () => {
    const save = deferred();
    api.getUser.mockImplementation(id => Promise.resolve(detail(id)));
    api.assignRoles.mockReturnValue(save.promise);
    const store = await mount([...Object.values(PERMISSIONS)]);
    const rows = () => container.querySelectorAll('tbody tr');
    click(rows()[0]);
    await flush();
    click(container.querySelector('#udp-role-r1'));
    click(byText(container, 'Lưu vai trò'));
    click(rows()[1]);
    await flush();
    save.resolve(detail('u1', { rowVersion: 5, roles: [{ id: 'r1', code: 'doctor', name: 'Bác sĩ' }] }));
    await flush();
    expect(container.querySelector('.c-detail-panel__title').textContent).toBe('Tên u2');
    expect(store.getState().admin.detail.id).toBe('u2');
    expect(store.getState().admin.detail.rowVersion).toBe(1);
    expect(store.getState().admin.items[0].rowVersion).toBe(5);
  });

  it('tạo tài khoản: initialPassword không vào Redux store', async () => {
    api.createUser.mockResolvedValue({ user: summary('u9'), initialPassword: 'Mk#SECRET99' });
    const store = await mount();
    click(byText(container, 'Tạo tài khoản'));
    act(() => {
      setInput(container.querySelector('#cu-fullname'), 'Người Chín');
      setInput(container.querySelector('#cu-email'), 'u9@x.vn');
    });
    click(container.querySelector('#cu-role-r1'));
    act(() => { container.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
    await flush();
    expect(container.textContent).toContain('Mk#SECRET99');
    expect(JSON.stringify(store.getState())).not.toContain('Mk#SECRET99');
  });

  it('thiếu roles.read/permissions.read: báo lý do, không gọi API catalog', async () => {
    const roles = require('feature/Roles/api'); // eslint-disable-line global-require
    roles.listRoles.mockClear();
    roles.listPermissions.mockClear();
    await mount([PERMISSIONS.USERS_READ]);
    expect(roles.listRoles).not.toHaveBeenCalled();
    expect(roles.listPermissions).not.toHaveBeenCalled();
    expect(container.textContent).toContain('roles.read');
  });

  it('thiếu users.create: không có nút Tạo tài khoản', async () => {
    await mount([PERMISSIONS.USERS_READ]);
    expect(byText(container, '+ Tạo tài khoản')).toBeUndefined();
  });
});

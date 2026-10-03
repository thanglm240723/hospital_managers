import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware, combineReducers } from 'redux';
import thunk from 'redux-thunk';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import rolesReducer from '../redux/reducer';
import * as api from '../api';
import RolesContainer from '../Container';

jest.mock('feature/Shell', () => ({ AppLayout: ({ children }) => <div>{children}</div> })); // eslint-disable-line react/prop-types
jest.mock('../api', () => ({
  listRoles: jest.fn(), listPermissions: jest.fn(), createRole: jest.fn(), renameRole: jest.fn(), updateRolePermissions: jest.fn(),
}));

const roles = [
  { id: 'a', code: 'a', name: 'Vai A', isSystem: false, permissionCodes: ['x.read'], rowVersion: 1 },
  { id: 'b', code: 'b', name: 'Vai B', isSystem: false, permissionCodes: [], rowVersion: 1 },
];
const flush = () => act(() => Promise.resolve());
const click = el => act(() => { el.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
const byText = (c, t) => Array.from(c.querySelectorAll('button')).find(b => b.textContent.indexOf(t) !== -1);

let container;
async function mount() {
  const store = createStore(
    combineReducers({ roles: rolesReducer, auth: (s = { permissions: [PERMISSIONS.ROLES_READ, PERMISSIONS.ROLES_MANAGE, PERMISSIONS.PERMISSIONS_READ] }) => s }),
    applyMiddleware(thunk),
  );
  container = document.createElement('div');
  document.body.appendChild(container);
  act(() => { ReactDOM.render(<Provider store={store}><RolesContainer /></Provider>, container); });
  await flush();
  return store;
}

afterEach(() => { ReactDOM.unmountComponentAtNode(container); container.remove(); });

describe('Roles Container', () => {
  beforeEach(() => {
    api.listPermissions.mockReset().mockResolvedValue([{ code: 'x.read', group: 'G', description: 'Xem X' }]);
  });

  it('lỗi loadRoles hiện thông báo và nút Thử lại nạp lại được', async () => {
    api.listRoles.mockReset().mockRejectedValueOnce(new Error('net')).mockResolvedValueOnce(roles);
    await mount();
    expect(container.textContent).toContain('Không tải được danh sách vai trò');
    click(byText(container, 'Thử lại'));
    await flush();
    expect(container.textContent).not.toContain('Không tải được danh sách vai trò');
    expect(container.textContent).toContain('Vai B');
  });

  it('có thay đổi chưa lưu: hỏi trước khi chuyển role; Ở lại giữ nguyên, Bỏ thì chuyển', async () => {
    api.listRoles.mockReset().mockResolvedValue(roles);
    const store = await mount();
    click(container.querySelector('[id="role-perm-x.read"]'));
    click(byText(container, 'Vai B'));
    expect(container.textContent).toContain('thay đổi chưa lưu');
    expect(store.getState().roles.selectedId).not.toBe('b');
    click(byText(container, 'Ở lại'));
    expect(container.textContent).not.toContain('Bỏ thay đổi và chuyển');
    click(byText(container, 'Vai B'));
    click(byText(container, 'Bỏ thay đổi và chuyển'));
    expect(store.getState().roles.selectedId).toBe('b');
  });
});

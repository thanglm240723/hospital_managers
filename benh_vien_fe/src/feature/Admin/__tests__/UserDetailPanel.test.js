import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import UserDetailPanel from '../component/UserDetailPanel';
import CreateUserDialog from '../component/CreateUserDialog';

/* eslint-disable react/prop-types */
jest.mock('feature/Shell', () => ({
  AppLayout: ({ children }) => <div>{children}</div>,
  ConfirmDialog: ({ title, onConfirm }) => <div role="dialog"><button type="button" onClick={onConfirm}>{`Xác nhận: ${title}`}</button></div>,
}));
/* eslint-enable react/prop-types */

const ALL = [PERMISSIONS.USERS_ROLES_MANAGE, PERMISSIONS.USERS_PERMISSIONS_MANAGE, PERMISSIONS.USERS_ACTIVATE];
const roleOptions = [
  { id: 'r1', code: 'doctor', name: 'Bác sĩ', permissionCodes: ['a.read'] },
  { id: 'r2', code: 'cashier', name: 'Thu ngân', permissionCodes: [] },
];
const permissions = [
  { code: 'a.read', group: 'G', description: 'Xem A' },
  { code: 'b.read', group: 'G', description: 'Xem B' },
];
const user = {
  id: 'u1',
  email: 'u1@x.vn',
  fullName: 'Người Một',
  isActive: true,
  mustChangePassword: false,
  roles: [{ id: 'r1', code: 'doctor', name: 'Bác sĩ' }],
  permissionGrants: [{ code: 'a.read', reason: 'Trực thay' }],
  effectivePermissions: ['a.read'],
  rowVersion: 3,
};

let container;
let store;
const flush = () => act(() => Promise.resolve());
const setInput = (el, value) => {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
  setter.call(el, value);
  el.dispatchEvent(new Event('input', { bubbles: true }));
};
const click = el => act(() => { el.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
const button = text => Array.from(container.querySelectorAll('button')).find(b => b.textContent.indexOf(text) !== -1);
const row = code => container.querySelector(`[data-code="${code}"]`);

function render(element, permissionList = ALL) {
  store = createStore((s = { auth: { permissions: permissionList } }) => s);
  container = document.createElement('div');
  document.body.appendChild(container);
  act(() => { ReactDOM.render(<Provider store={store}>{element}</Provider>, container); });
}

afterEach(() => {
  ReactDOM.unmountComponentAtNode(container);
  container.remove();
});

const handlers = (extra = {}) => ({
  user,
  roleOptions,
  permissions,
  onAssignRoles: jest.fn(),
  onGrantPermission: jest.fn(),
  onRevokePermission: jest.fn(),
  onActivate: jest.fn(),
  onDeactivate: jest.fn(),
  onReload: jest.fn(),
  ...extra,
});

const err = (status, code) => {
  const e = new Error('x');
  e.response = { status, data: { code, title: 'Lỗi máy chủ' } };
  return e;
};

describe('UserDetailPanel — vai trò', () => {
  it('lưu vai trò gửi id, roleIds thật và rowVersion', async () => {
    const p = handlers();
    p.onAssignRoles.mockResolvedValue({ ...user, rowVersion: 4, roles: [...user.roles, { id: 'r2', code: 'cashier', name: 'Thu ngân' }] });
    render(<UserDetailPanel {...p} />);
    click(container.querySelector('#udp-role-r2'));
    click(button('Lưu vai trò'));
    await flush();
    expect(p.onAssignRoles).toHaveBeenCalledWith('u1', ['r1', 'r2'], 3);
    expect(button('Lưu vai trò').disabled).toBe(true);
  });

  it('412: giữ lựa chọn, báo user_version_conflict, nạp lại, không tự retry', async () => {
    const p = handlers();
    p.onAssignRoles.mockRejectedValue(err(412, 'user_version_conflict'));
    render(<UserDetailPanel {...p} />);
    click(container.querySelector('#udp-role-r2'));
    click(button('Lưu vai trò'));
    await flush();
    expect(container.querySelector('#udp-role-r2').checked).toBe(true);
    expect(container.textContent).toContain('user_version_conflict');
    expect(p.onAssignRoles).toHaveBeenCalledTimes(1);
    expect(button('Lưu vai trò').disabled).toBe(false);
    click(button('Nạp lại bản mới'));
    expect(p.onReload).toHaveBeenCalledTimes(1);
  });

  it('sau nạp lại bản mới (rowVersion đổi) cảnh báo khác biệt và khóa lưu đến khi bỏ chỉnh sửa', async () => {
    const p = handlers();
    p.onAssignRoles.mockRejectedValue(err(412, 'user_version_conflict'));
    render(<UserDetailPanel {...p} />);
    click(container.querySelector('#udp-role-r2'));
    click(button('Lưu vai trò'));
    await flush();
    const fresh = { ...user, rowVersion: 9, roles: [] };
    act(() => { ReactDOM.render(<Provider store={store}><UserDetailPanel {...p} user={fresh} /></Provider>, container); });
    expect(container.textContent).toContain('đã thay đổi so với bản bạn đang sửa');
    expect(container.querySelector('#udp-role-r2').checked).toBe(true);
    expect(button('Lưu vai trò').disabled).toBe(true);
  });

  it('thiếu users.roles.manage: không có checkbox/nút lưu vai trò', () => {
    render(<UserDetailPanel {...handlers()} />, []);
    expect(container.querySelector('#udp-role-r1')).toBeNull();
    expect(button('Lưu vai trò')).toBeUndefined();
    expect(container.textContent).toContain('Bác sĩ');
  });

  it('thiếu roles.read (roleOptionsError): báo lý do, không tự cấp quyền ngầm', () => {
    render(<UserDetailPanel {...handlers({ roleOptions: [], roleOptionsError: 'tài khoản không có quyền roles.read' })} />);
    expect(container.textContent).toContain('roles.read');
    expect(button('Lưu vai trò')).toBeUndefined();
  });
});

describe('UserDetailPanel — nguồn quyền', () => {
  it('phân biệt quyền cấp thêm và quyền từ vai trò, kèm lý do', () => {
    render(<UserDetailPanel {...handlers()} />);
    expect(row('a.read').textContent).toContain('Cấp thêm: Trực thay');
    expect(row('a.read').textContent).toContain('Từ vai trò: Bác sĩ');
    expect(row('b.read').textContent).toContain('Không có quyền');
  });

  it('grant/revoke cần lý do; gửi một code, reason và rowVersion', async () => {
    const p = handlers();
    p.onGrantPermission.mockResolvedValue(user);
    render(<UserDetailPanel {...p} />);
    const grant = () => row('b.read').querySelector('button');
    expect(grant().disabled).toBe(true);
    act(() => setInput(container.querySelector('#udp-reason'), '  Cần xem B  '));
    expect(grant().disabled).toBe(false);
    click(grant());
    await flush();
    expect(p.onGrantPermission).toHaveBeenCalledWith('u1', 'b.read', 'Cần xem B', 3);
    expect(container.querySelector('#udp-reason').value).toBe('');
  });

  it('revoke grant còn quyền do vai trò: vẫn hiển thị có quyền và nguồn vai trò', async () => {
    const p = handlers();
    const afterRevoke = { ...user, permissionGrants: [], effectivePermissions: ['a.read'], rowVersion: 4 };
    p.onRevokePermission.mockResolvedValue(afterRevoke);
    const { rerenderWith } = { rerenderWith: next => act(() => { ReactDOM.render(<Provider store={store}><UserDetailPanel {...p} user={next} /></Provider>, container); }) };
    render(<UserDetailPanel {...p} />);
    act(() => setInput(container.querySelector('#udp-reason'), 'Hết trực'));
    click(button('Thu hồi quyền cấp thêm'));
    await flush();
    expect(p.onRevokePermission).toHaveBeenCalledWith('u1', 'a.read', 'Hết trực', 3);
    rerenderWith(afterRevoke);
    expect(row('a.read').textContent).toContain('Có quyền');
    expect(row('a.read').textContent).toContain('Từ vai trò: Bác sĩ');
    expect(row('a.read').textContent).not.toContain('Cấp thêm:');
    expect(container.textContent).toContain('không loại bỏ quyền mà vai trò');
  });

  it('412 khi cấp quyền: giữ lý do, báo nạp lại, không retry', async () => {
    const p = handlers();
    p.onGrantPermission.mockRejectedValue(err(412, 'user_version_conflict'));
    render(<UserDetailPanel {...p} />);
    act(() => setInput(container.querySelector('#udp-reason'), 'Lý do'));
    click(row('b.read').querySelector('button'));
    await flush();
    expect(container.querySelector('#udp-reason').value).toBe('Lý do');
    expect(container.textContent).toContain('Nạp lại bản mới');
    expect(p.onGrantPermission).toHaveBeenCalledTimes(1);
    expect(row('b.read').querySelector('button').disabled).toBe(false);
  });

  it('thiếu catalog: hiện lý do, vẫn liệt kê quyền hiện có, không có nút cấp thêm', () => {
    render(<UserDetailPanel {...handlers({ permissions: [], permissionsError: 'tài khoản không có quyền permissions.read' })} />);
    expect(container.textContent).toContain('permissions.read');
    expect(row('a.read')).not.toBeNull();
    expect(row('b.read')).toBeNull();
    act(() => setInput(container.querySelector('#udp-reason'), 'x'));
    expect(button('Cấp thêm')).toBeUndefined();
    expect(button('Thu hồi quyền cấp thêm')).toBeDefined();
  });

  it('thiếu users.permissions.manage: không có ô lý do hay nút', () => {
    render(<UserDetailPanel {...handlers()} />, [PERMISSIONS.USERS_ROLES_MANAGE]);
    expect(container.querySelector('#udp-reason')).toBeNull();
    expect(button('Thu hồi quyền cấp thêm')).toBeUndefined();
  });
});

describe('UserDetailPanel — khóa tài khoản và mật khẩu', () => {
  it('nút cấp lại mật khẩu bị vô hiệu kèm thông báo chưa hỗ trợ', () => {
    render(<UserDetailPanel {...handlers()} />);
    expect(button('Cấp lại mật khẩu tạm').disabled).toBe(true);
    expect(container.textContent).toContain('Chưa hỗ trợ');
  });

  it('khóa gọi deactivate(id) không version; lỗi self_action_forbidden hiện mã và busy không treo', async () => {
    const p = handlers();
    p.onDeactivate.mockRejectedValue(err(409, 'self_action_forbidden'));
    render(<UserDetailPanel {...p} />);
    click(button('Khóa tài khoản'));
    click(button('Xác nhận: Khóa tài khoản?'));
    await flush();
    expect(p.onDeactivate).toHaveBeenCalledWith('u1');
    expect(container.textContent).toContain('self_action_forbidden');
    expect(button('Khóa tài khoản').disabled).toBe(false);
  });

  it('user đã khóa: mở khóa gọi activate', async () => {
    const p = handlers({ user: { ...user, isActive: false } });
    p.onActivate.mockResolvedValue({});
    render(<UserDetailPanel {...p} />);
    click(button('Mở khóa tài khoản'));
    click(button('Xác nhận: Mở khóa tài khoản?'));
    await flush();
    expect(p.onActivate).toHaveBeenCalledWith('u1');
  });

  it('thiếu users.activate: không có nút khóa', () => {
    render(<UserDetailPanel {...handlers()} />, [PERMISSIONS.USERS_ROLES_MANAGE]);
    expect(button('Khóa tài khoản')).toBeUndefined();
  });

  it('lưu xong sau khi unmount không gây lỗi setState', async () => {
    const p = handlers();
    let resolve;
    p.onAssignRoles.mockReturnValue(new Promise((r) => { resolve = r; }));
    const spy = jest.spyOn(console, 'error').mockImplementation(() => {});
    render(<UserDetailPanel {...p} />);
    click(container.querySelector('#udp-role-r2'));
    click(button('Lưu vai trò'));
    ReactDOM.unmountComponentAtNode(container);
    resolve({ ...user, rowVersion: 4 });
    await flush();
    expect(spy).not.toHaveBeenCalled();
    spy.mockRestore();
    render(<div />); // để afterEach có container hợp lệ
  });
});

describe('CreateUserDialog', () => {
  const fill = () => {
    act(() => { setInput(container.querySelector('#cu-fullname'), 'Nguyễn An'); setInput(container.querySelector('#cu-email'), 'an@x.vn'); });
  };
  const submit = () => act(() => { container.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });

  it('không có ô mật khẩu; thiếu email/họ tên/vai trò thì báo lỗi từng trường và không gọi API', async () => {
    const onCreate = jest.fn();
    render(<CreateUserDialog onCreate={onCreate} onClose={jest.fn()} roleOptions={roleOptions} />);
    expect(container.querySelector('input[type="password"]')).toBeNull();
    submit();
    await flush();
    expect(container.querySelector('[data-field="email"]')).not.toBeNull();
    expect(container.querySelector('[data-field="fullName"]')).not.toBeNull();
    expect(container.querySelector('[data-field="roleIds"]')).not.toBeNull();
    expect(onCreate).not.toHaveBeenCalled();
  });

  it('gửi đúng body; sau 201 hiện initialPassword một lần kèm cảnh báo và nút sao chép', async () => {
    const onCreate = jest.fn().mockResolvedValue({ user: { email: 'an@x.vn' }, initialPassword: 'Mk#123abc' });
    const writeText = jest.fn().mockResolvedValue();
    Object.defineProperty(navigator, 'clipboard', { value: { writeText }, configurable: true });
    render(<CreateUserDialog onCreate={onCreate} onClose={jest.fn()} roleOptions={roleOptions} />);
    fill();
    click(container.querySelector('#cu-role-r2'));
    submit();
    await flush();
    expect(onCreate).toHaveBeenCalledWith({ fullName: 'Nguyễn An', email: 'an@x.vn', roleIds: ['r2'] });
    expect(container.textContent).toContain('Mk#123abc');
    expect(container.textContent).toContain('không xem lại được');
    click(button('Sao chép'));
    await flush();
    expect(writeText).toHaveBeenCalledWith('Mk#123abc');
  });

  it('mật khẩu không còn sau khi đóng dialog (unmount) và không vào storage', async () => {
    const onCreate = jest.fn().mockResolvedValue({ user: { email: 'an@x.vn' }, initialPassword: 'Mk#123abc' });
    render(<CreateUserDialog onCreate={onCreate} onClose={jest.fn()} roleOptions={roleOptions} />);
    fill();
    click(container.querySelector('#cu-role-r1'));
    submit();
    await flush();
    expect(JSON.stringify(window.localStorage) + JSON.stringify(window.sessionStorage)).not.toContain('Mk#123abc');
    ReactDOM.unmountComponentAtNode(container);
    expect(container.textContent).not.toContain('Mk#123abc');
    render(<div />);
  });

  it('409 email_taken hiện mã lỗi, form giữ nguyên và không tự gửi lại', async () => {
    const onCreate = jest.fn().mockRejectedValue(err(409, 'email_taken'));
    render(<CreateUserDialog onCreate={onCreate} onClose={jest.fn()} roleOptions={roleOptions} />);
    fill();
    click(container.querySelector('#cu-role-r1'));
    submit();
    await flush();
    expect(container.textContent).toContain('email_taken');
    expect(container.querySelector('#cu-email').value).toBe('an@x.vn');
    expect(container.querySelector('button[type="submit"]').disabled).toBe(false);
    expect(onCreate).toHaveBeenCalledTimes(1);
  });

  it('400 có errors theo trường: hiện dưới đúng trường', async () => {
    const e = new Error('x');
    e.response = { status: 400, data: { code: 'validation_error', title: 'Dữ liệu không hợp lệ', errors: { Email: ['Email không hợp lệ.'] } } };
    render(<CreateUserDialog onCreate={jest.fn().mockRejectedValue(e)} onClose={jest.fn()} roleOptions={roleOptions} />);
    fill();
    click(container.querySelector('#cu-role-r1'));
    submit();
    await flush();
    expect(container.querySelector('[data-field="email"]').textContent).toBe('Email không hợp lệ.');
  });

  it('thiếu danh sách vai trò: báo lý do', () => {
    render(<CreateUserDialog onCreate={jest.fn()} onClose={jest.fn()} roleOptionsReason="tài khoản không có quyền roles.read" />);
    expect(container.textContent).toContain('roles.read');
  });
});

describe('UserDetailPanel — vòng fix 1', () => {
  it('grant thành công khi đang có draft vai trò chưa lưu: không báo xung đột giả, vẫn lưu được vai trò', async () => {
    const p = handlers();
    p.onGrantPermission.mockResolvedValue({ ...user, rowVersion: 4 });
    render(<UserDetailPanel {...p} />);
    click(container.querySelector('#udp-role-r2'));
    act(() => setInput(container.querySelector('#udp-reason'), 'Lý do'));
    click(row('b.read').querySelector('button'));
    await flush();
    act(() => { ReactDOM.render(<Provider store={store}><UserDetailPanel {...p} user={{ ...user, rowVersion: 4 }} /></Provider>, container); });
    expect(container.textContent).not.toContain('đã thay đổi so với bản bạn đang sửa');
    expect(button('Lưu vai trò').disabled).toBe(false);
  });

  it('thiếu roleOptions: quyền chỉ từ grant không gắn "Từ vai trò"; quyền hiệu lực không grant báo không xác định nguồn', () => {
    const u = { ...user, effectivePermissions: ['a.read', 'b.read'], permissionGrants: [{ code: 'a.read', reason: 'x' }] };
    render(<UserDetailPanel {...handlers({ user: u, roleOptions: [], roleOptionsError: 'tài khoản không có quyền roles.read' })} />);
    expect(row('a.read').textContent).not.toContain('Từ vai trò');
    expect(row('b.read').textContent).toContain('Không xác định nguồn vai trò (thiếu roles.read)');
  });

  it('sao chép lỗi: hiện hướng dẫn chép tay', async () => {
    Object.defineProperty(navigator, 'clipboard', { value: { writeText: jest.fn().mockRejectedValue(new Error('x')) }, configurable: true });
    const onCreate = jest.fn().mockResolvedValue({ user: { email: 'a@x.vn' }, initialPassword: 'Pw#1' });
    render(<CreateUserDialog onCreate={onCreate} onClose={jest.fn()} roleOptions={roleOptions} />);
    act(() => { setInput(container.querySelector('#cu-fullname'), 'A'); setInput(container.querySelector('#cu-email'), 'a@x.vn'); });
    click(container.querySelector('#cu-role-r1'));
    act(() => { container.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
    await flush();
    click(button('Sao chép'));
    await flush();
    expect(container.textContent).toContain('Không sao chép được, hãy chép tay');
  });
});

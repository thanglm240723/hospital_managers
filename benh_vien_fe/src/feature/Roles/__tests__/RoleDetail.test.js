import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import RoleDetail from '../component/RoleDetail';
import CreateRoleDialog from '../component/CreateRoleDialog';

const role = {
  id: 'r1', code: 'custom', name: 'Tùy chỉnh', isSystem: false, permissionCodes: ['a.read'], rowVersion: 3,
};
const permissions = [
  { code: 'a.read', group: 'A', description: 'Xem A' },
  { code: 'b.read', group: 'A', description: 'Xem B' },
];

let container;
const flush = () => act(() => Promise.resolve());
const setInput = (el, value) => {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
  setter.call(el, value);
  el.dispatchEvent(new Event('input', { bubbles: true }));
};
const click = el => act(() => { el.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
const perm = code => container.querySelector(`[id="role-perm-${code}"]`);
const saveButton = () => Array.from(container.querySelectorAll('button')).find(b => /Lưu|Đang lưu/.test(b.textContent));
const submitForm = () => act(() => { container.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });

function render(element) {
  container = document.createElement('div');
  document.body.appendChild(container);
  act(() => { ReactDOM.render(element, container); });
}

afterEach(() => {
  ReactDOM.unmountComponentAtNode(container);
  container.remove();
});

const props = (extra = {}) => ({
  role, permissions, canManage: true, onRename: jest.fn(), onSavePermissions: jest.fn(), onClone: jest.fn(), ...extra,
});

describe('RoleDetail', () => {
  it('thiếu roles.manage: checkbox bị khóa, không có nút lưu', () => {
    render(<RoleDetail {...props({ canManage: false })} />);
    expect(perm('a.read').disabled).toBe(true);
    expect(saveButton()).toBeUndefined();
    expect(container.textContent).toContain('roles.manage');
  });

  it('vai trò hệ thống luôn chỉ đọc kể cả có roles.manage', () => {
    render(<RoleDetail {...props({ role: { ...role, isSystem: true } })} />);
    expect(perm('a.read').disabled).toBe(true);
    expect(saveButton()).toBeUndefined();
  });

  it('thiếu catalog: báo lý do, vẫn hiện quyền hiện có, không tự thêm quyền', () => {
    render(<RoleDetail {...props({ permissions: [], permissionsError: 'tài khoản không có quyền permissions.read' })} />);
    expect(container.textContent).toContain('Không tải được danh mục quyền');
    expect(perm('a.read').checked).toBe(true);
    expect(perm('b.read')).toBeNull();
  });

  it('lưu quyền gửi đúng id, mã quyền và rowVersion', async () => {
    const p = props();
    p.onSavePermissions.mockResolvedValue({});
    render(<RoleDetail {...p} />);
    click(perm('b.read'));
    click(saveButton());
    await flush();
    expect(p.onSavePermissions).toHaveBeenCalledWith('r1', ['a.read', 'b.read'], 3);
    expect(p.onRename).not.toHaveBeenCalled();
  });

  it('lỗi 412: bỏ saving, giữ chỉnh sửa, hiện yêu cầu nạp lại', async () => {
    const p = props();
    const error = new Error('x');
    error.response = { status: 412, data: { code: 'role_version_conflict', title: 'Phiên bản cũ' } };
    p.onSavePermissions.mockRejectedValue(error);
    render(<RoleDetail {...p} />);
    click(perm('b.read'));
    click(saveButton());
    await flush();
    expect(perm('b.read').checked).toBe(true);
    expect(container.textContent).toContain('role_version_conflict');
    expect(container.textContent).toContain('Nạp lại');
    expect(saveButton().textContent).toBe('Lưu thay đổi');
    expect(saveButton().disabled).toBe(false);
  });

  it('báo thay đổi chưa lưu qua onDirtyChange', () => {
    const onDirtyChange = jest.fn();
    render(<RoleDetail {...props({ onDirtyChange })} />);
    click(perm('b.read'));
    expect(onDirtyChange).toHaveBeenLastCalledWith(true);
  });
});

describe('CreateRoleDialog', () => {
  it('bắt buộc nhập Code và Tên, gửi quyền khởi tạo từ vai trò nguồn', async () => {
    const onCreate = jest.fn().mockResolvedValue({});
    render(<CreateRoleDialog onCreate={onCreate} onCancel={jest.fn()} initialPermissionCodes={['a.read']} sourceName="Nguồn" />);
    const submit = container.querySelector('button[type="submit"]');
    expect(submit.disabled).toBe(true);
    act(() => { setInput(container.querySelector('#new-role-code'), 'moi'); setInput(container.querySelector('#new-role-name'), 'Mới'); });
    expect(submit.disabled).toBe(false);
    submitForm();
    await flush();
    expect(onCreate).toHaveBeenCalledWith({ code: 'moi', name: 'Mới', permissionCodes: ['a.read'] });
  });

  it('409 role_code_taken hiện mã lỗi', async () => {
    const error = new Error('x');
    error.response = { status: 409, data: { code: 'role_code_taken', title: 'Mã đã tồn tại' } };
    render(<CreateRoleDialog onCreate={jest.fn().mockRejectedValue(error)} onCancel={jest.fn()} />);
    act(() => { setInput(container.querySelector('#new-role-code'), 'moi'); setInput(container.querySelector('#new-role-name'), 'Mới'); });
    submitForm();
    await flush();
    expect(container.textContent).toContain('role_code_taken');
  });
});

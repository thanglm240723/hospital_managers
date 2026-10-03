import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore } from 'redux';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import { getFacilityTree } from 'feature/Facilities/api';
import * as client from '../api/staffProfileClient';
import StaffProfileSection from '../component/StaffProfileSection';

jest.mock('../api/staffProfileClient', () => ({
  getStaffProfile: jest.fn(), createStaffProfile: jest.fn(), updateStaffProfile: jest.fn(), setWorkScopes: jest.fn(),
}));
jest.mock('feature/Facilities/api', () => ({ getFacilityTree: jest.fn() }));

const MANAGE = [PERMISSIONS.STAFF_PROFILES_READ, PERMISSIONS.STAFF_PROFILES_MANAGE, PERMISSIONS.FACILITIES_READ];
const tree = [{
  id: 'b1',
  name: 'Cơ sở 1',
  departments: [
    { id: 'd1', name: 'Khoa Nội', isActive: true },
    { id: 'd2', name: 'Khoa Ngoại', isActive: true },
    { id: 'd3', name: 'Khoa Cũ', isActive: false },
  ],
}];
const scope = {
  branchId: 'b1',
  branchName: 'Cơ sở 1',
  departmentId: 'd1',
  departmentName: 'Khoa Nội',
  departmentKind: 'clinical',
};
const profile = (extra = {}) => ({
  id: 'p1', userId: 'u1', staffCode: 'NV1', isActive: true, rowVersion: 3, workScopes: [scope], ...extra,
});
const fail = (status, code, errors) => ({ response: { status, data: { code, title: 't', errors } } });

let container;
const flush = () => act(() => Promise.resolve());
const click = el => act(() => { el.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
const button = text => Array.from(container.querySelectorAll('button')).find(b => b.textContent.indexOf(text) !== -1);
const setInput = (el, value) => {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
  setter.call(el, value);
  el.dispatchEvent(new Event('input', { bubbles: true }));
};

let store;
const element = userId => (
  <Provider store={store}><StaffProfileSection userId={userId} /></Provider>
);
async function mount(perms = MANAGE, userId = 'u1') {
  container = document.createElement('div');
  document.body.appendChild(container);
  store = createStore((s = { auth: { permissions: perms } }) => s);
  act(() => { ReactDOM.render(element(userId), container); });
  await flush();
}

beforeEach(() => {
  Object.values(client).filter(f => typeof f === 'function').forEach(f => f.mockReset());
  getFacilityTree.mockReset().mockResolvedValue(tree);
});
afterEach(() => {
  ReactDOM.unmountComponentAtNode(container);
  container.remove();
});

describe('StaffProfileSection', () => {
  it('404 -> form tạo khi có manage; tạo thành công hiện hồ sơ', async () => {
    client.getStaffProfile.mockRejectedValue(fail(404, 'staff_profile_not_found'));
    client.createStaffProfile.mockResolvedValue(profile({ workScopes: [] }));
    await mount();
    expect(container.textContent).toContain('Chưa có hồ sơ nhân sự');
    setInput(container.querySelector('#sp-code'), 'NV1');
    await act(async () => { container.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
    expect(client.createStaffProfile).toHaveBeenCalledWith('u1', { staffCode: 'NV1', isActive: true });
    expect(container.textContent).toContain('Mã nhân sự: NV1');
  });

  it('404 không có manage -> không có form tạo', async () => {
    client.getStaffProfile.mockRejectedValue(fail(404, 'staff_profile_not_found'));
    await mount([PERMISSIONS.STAFF_PROFILES_READ]);
    expect(container.textContent).toContain('Chưa có hồ sơ nhân sự');
    expect(container.querySelector('form')).toBeNull();
  });

  it('chọn 2 khoa rồi lưu gọi setWorkScopes với rowVersion', async () => {
    client.getStaffProfile.mockResolvedValue(profile({ workScopes: [] }));
    client.setWorkScopes.mockResolvedValue(profile());
    await mount();
    expect(container.querySelector('#sp-dep-d3')).toBeNull();
    click(container.querySelector('#sp-dep-d1'));
    click(container.querySelector('#sp-dep-d2'));
    await act(async () => { click(button('Lưu phạm vi')); });
    expect(client.setWorkScopes).toHaveBeenCalledWith('u1', ['d1', 'd2'], 3);
  });

  it('invalid_departments hiện danh sách id', async () => {
    client.getStaffProfile.mockResolvedValue(profile());
    client.setWorkScopes.mockRejectedValue(fail(400, 'invalid_departments', { departmentIds: ['dx', 'dy'] }));
    await mount();
    await act(async () => { click(button('Lưu phạm vi')); });
    expect(container.textContent).toContain('invalid_departments');
    expect(container.textContent).toContain('dx, dy');
  });

  it('invalid_departments hiện tên khoa khi cây đã tải', async () => {
    client.getStaffProfile.mockResolvedValue(profile());
    client.setWorkScopes.mockRejectedValue(fail(400, 'invalid_departments', { departmentIds: ['d3', 'dz'] }));
    await mount();
    await act(async () => { click(button('Lưu phạm vi')); });
    expect(container.textContent).toContain('Khoa Cũ, dz');
  });

  it('tạo hồ sơ gặp 409 staff_profile_exists -> nạp lại và hiện hồ sơ', async () => {
    client.getStaffProfile.mockRejectedValueOnce(fail(404, 'staff_profile_not_found')).mockResolvedValueOnce(profile());
    client.createStaffProfile.mockRejectedValue(fail(409, 'staff_profile_exists'));
    await mount();
    setInput(container.querySelector('#sp-code'), 'NV1');
    await act(async () => { container.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
    expect(client.getStaffProfile).toHaveBeenCalledTimes(2);
    expect(client.createStaffProfile).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain('Hồ sơ đã được tạo bởi người khác, đã nạp lại');
    expect(container.textContent).toContain('Mã nhân sự: NV1');
  });

  const submitEdit = async (code) => {
    click(button('Sửa mã/trạng thái'));
    setInput(container.querySelector('#sp-code'), code);
    await act(async () => { container.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
  };

  it('cập nhật mã/trạng thái gửi If-Match rowVersion', async () => {
    client.getStaffProfile.mockResolvedValue(profile());
    client.updateStaffProfile.mockResolvedValue(profile({ staffCode: 'NV9', rowVersion: 4 }));
    await mount();
    await submitEdit('NV9');
    expect(client.updateStaffProfile).toHaveBeenCalledWith('u1', { staffCode: 'NV9', isActive: true }, 3);
    expect(container.textContent).toContain('Mã nhân sự: NV9');
  });

  it('cập nhật gặp 412 -> nạp lại hồ sơ, không retry', async () => {
    client.getStaffProfile.mockResolvedValueOnce(profile()).mockResolvedValueOnce(profile({ rowVersion: 8 }));
    client.updateStaffProfile.mockRejectedValue(fail(412, 'staff_profile_version_conflict'));
    await mount();
    await submitEdit('NV9');
    expect(client.getStaffProfile).toHaveBeenCalledTimes(2);
    expect(client.updateStaffProfile).toHaveBeenCalledTimes(1);
    expect(container.textContent).toContain('staff_profile_version_conflict');
  });

  it('cập nhật gặp 409 staff_code_taken -> hiện thông báo', async () => {
    client.getStaffProfile.mockResolvedValue(profile());
    client.updateStaffProfile.mockRejectedValue(fail(409, 'staff_code_taken'));
    await mount();
    await submitEdit('NV9');
    expect(container.textContent).toContain('staff_code_taken');
    expect(client.getStaffProfile).toHaveBeenCalledTimes(1);
  });

  it('412 nạp lại hồ sơ và giữ lựa chọn, không retry', async () => {
    client.getStaffProfile.mockResolvedValueOnce(profile()).mockResolvedValueOnce(profile({ rowVersion: 9 }));
    client.setWorkScopes.mockRejectedValue(fail(412, 'staff_profile_version_conflict'));
    await mount();
    click(container.querySelector('#sp-dep-d2'));
    await act(async () => { click(button('Lưu phạm vi')); });
    expect(client.getStaffProfile).toHaveBeenCalledTimes(2);
    expect(client.setWorkScopes).toHaveBeenCalledTimes(1);
    expect(container.querySelector('#sp-dep-d2').checked).toBe(true);
    expect(container.textContent).toContain('staff_profile_version_conflict');
    client.setWorkScopes.mockResolvedValue(profile());
    await act(async () => { click(button('Lưu phạm vi')); });
    expect(client.setWorkScopes).toHaveBeenLastCalledWith('u1', ['d1', 'd2'], 9);
  });

  it('thiếu facilities.read -> thông báo, vẫn hiện phạm vi hiện có', async () => {
    client.getStaffProfile.mockResolvedValue(profile());
    await mount([PERMISSIONS.STAFF_PROFILES_READ, PERMISSIONS.STAFF_PROFILES_MANAGE]);
    expect(getFacilityTree).not.toHaveBeenCalled();
    expect(container.textContent).toContain('Thiếu quyền facilities.read nên không tải được danh sách khoa');
    expect(container.textContent).toContain('Khoa Nội');
  });

  it('hồ sơ không phạm vi hoặc đang tắt -> cảnh báo', async () => {
    client.getStaffProfile.mockResolvedValue(profile({ workScopes: [] }));
    await mount();
    expect(container.textContent).toContain('chưa có cơ sở/khoa làm việc');
  });

  it('đổi userId khi request cũ chưa về -> không hiện dữ liệu user cũ', async () => {
    let resolveOld;
    client.getStaffProfile
      .mockImplementationOnce(() => new Promise((resolve) => { resolveOld = resolve; }))
      .mockResolvedValueOnce(profile({ userId: 'u2', staffCode: 'NV2' }));
    await mount(MANAGE, 'u1');
    act(() => { ReactDOM.render(element('u2'), container); });
    await flush();
    await act(async () => { resolveOld(profile({ staffCode: 'CU' })); });
    expect(container.textContent).toContain('NV2');
    expect(container.textContent).not.toContain('CU');
  });
});

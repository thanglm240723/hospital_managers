import React from 'react';
import ReactDOM from 'react-dom';
import { act } from 'react-dom/test-utils';
import { Provider } from 'react-redux';
import { createStore, applyMiddleware, combineReducers } from 'redux';
import thunk from 'redux-thunk';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import facilitiesReducer from '../redux/reducer';
import * as api from '../api';
import FacilitiesContainer from '../Container';

jest.mock('feature/Shell', () => ({ AppLayout: ({ children }) => <div>{children}</div> })); // eslint-disable-line react/prop-types
jest.mock('../api', () => ({
  getFacilityTree: jest.fn(), createBranch: jest.fn(), createDepartment: jest.fn(), createRoom: jest.fn(), updateFacility: jest.fn(),
}));

const tree = (version = 1) => [{
  id: 'b1',
  code: 'CS1',
  name: 'Cơ sở Một',
  isActive: true,
  rowVersion: version,
  departments: [{
    id: 'd1',
    code: 'K1',
    name: 'Khoa Nội',
    kind: 'clinical',
    isActive: false,
    rowVersion: version,
    rooms: [{
      id: 'r1', code: 'P1', name: 'Phòng 1', isActive: true, rowVersion: version,
    }],
  }],
}];
const problem = (status, code) => ({ response: { status, data: { code, title: 'Lỗi' } } });
const flush = () => act(() => Promise.resolve());
const click = el => act(() => { el.dispatchEvent(new MouseEvent('click', { bubbles: true })); });
const byText = (c, t) => Array.from(c.querySelectorAll('button')).find(b => b.textContent.indexOf(t) !== -1);
const type = (el, value) => act(() => {
  const setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
  setter.call(el, value);
  el.dispatchEvent(new Event('input', { bubbles: true }));
});
const submit = async (c) => {
  act(() => { c.querySelector('form').dispatchEvent(new Event('submit', { bubbles: true, cancelable: true })); });
  await flush();
  await flush();
};

let container;
async function mount(permissions = [PERMISSIONS.FACILITIES_READ, PERMISSIONS.FACILITIES_MANAGE]) {
  const store = createStore(
    combineReducers({ facilities: facilitiesReducer, auth: (s = { permissions }) => s }),
    applyMiddleware(thunk),
  );
  container = document.createElement('div');
  document.body.appendChild(container);
  act(() => { ReactDOM.render(<Provider store={store}><FacilitiesContainer /></Provider>, container); });
  await flush();
  return store;
}

afterEach(() => { ReactDOM.unmountComponentAtNode(container); container.remove(); });

describe('Facilities Container', () => {
  beforeEach(() => {
    Object.values(api).filter(fn => typeof fn === 'function').forEach(fn => fn.mockReset());
    api.getFacilityTree.mockResolvedValue(tree());
  });

  it('tải thành công hiện cây, nhãn loại và badge Ngừng dùng', async () => {
    await mount();
    expect(container.textContent).toContain('Cơ sở Một');
    expect(container.textContent).toContain('Khoa Nội (Lâm sàng)');
    expect(container.textContent).toContain('Phòng 1');
    expect(container.textContent).toContain('Ngừng dùng');
  });

  it('lỗi tải hiện thông báo và Thử lại nạp lại được', async () => {
    api.getFacilityTree.mockReset().mockRejectedValueOnce(new Error('net')).mockResolvedValueOnce(tree());
    await mount();
    expect(container.textContent).toContain('Không tải được cơ cấu tổ chức');
    click(byText(container, 'Thử lại'));
    await flush();
    expect(container.textContent).not.toContain('Không tải được');
    expect(container.textContent).toContain('Cơ sở Một');
  });

  it('không có facilities.manage: ẩn mọi nút thêm/sửa', async () => {
    await mount([PERMISSIONS.FACILITIES_READ]);
    expect(container.textContent).toContain('Cơ sở Một');
    expect(byText(container, 'Thêm')).toBeUndefined();
    expect(byText(container, 'Sửa')).toBeUndefined();
  });

  it('tạo khoa: gọi createDepartment với branchId đúng rồi nạp lại cây', async () => {
    api.createDepartment.mockResolvedValue({ id: 'd2' });
    await mount();
    click(byText(container, 'Thêm khoa'));
    type(container.querySelector('#facility-code'), 'k2');
    type(container.querySelector('#facility-name'), 'Khoa Ngoại');
    await submit(container);
    expect(api.createDepartment).toHaveBeenCalledWith({
      branchId: 'b1', code: 'K2', name: 'Khoa Ngoại', kind: 'clinical',
    });
    expect(api.getFacilityTree).toHaveBeenCalledTimes(2);
    expect(container.querySelector('form')).toBeNull();
  });

  it('409 facility_code_taken hiện thông báo theo mã, giữ dialog', async () => {
    api.createBranch.mockRejectedValue(problem(409, 'facility_code_taken'));
    await mount();
    click(byText(container, 'Thêm cơ sở'));
    type(container.querySelector('#facility-code'), 'cs1');
    type(container.querySelector('#facility-name'), 'Trùng');
    await submit(container);
    expect(container.textContent).toContain('Mã đã tồn tại trong cùng cấp');
    expect(container.querySelector('form')).not.toBeNull();
    expect(api.getFacilityTree).toHaveBeenCalledTimes(1);
  });

  it('412: nạp lại cây, giữ dialog với giá trị đã nhập, không tự gửi lại', async () => {
    api.updateFacility.mockRejectedValue(problem(412, 'facility_version_conflict'));
    await mount();
    click(Array.from(container.querySelectorAll('button')).filter(b => b.textContent === 'Sửa')[0]);
    type(container.querySelector('#facility-name'), 'Tên mới');
    api.getFacilityTree.mockResolvedValue(tree(5));
    await submit(container);
    expect(api.updateFacility).toHaveBeenCalledTimes(1);
    expect(api.updateFacility).toHaveBeenCalledWith('branches', 'b1', { name: 'Tên mới', isActive: true }, 1);
    expect(api.getFacilityTree).toHaveBeenCalledTimes(2);
    expect(container.textContent).toContain('Dữ liệu đã thay đổi, đã nạp lại');
    expect(container.querySelector('#facility-name').value).toBe('Tên mới');
    // lần lưu lại dùng rowVersion mới
    await submit(container);
    expect(api.updateFacility).toHaveBeenLastCalledWith('branches', 'b1', { name: 'Tên mới', isActive: true }, 5);
  });
});

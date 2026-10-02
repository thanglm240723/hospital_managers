import rolesReducer, { ROLES_LIST_SUCCESS, ROLES_UPSERT, ROLES_SELECT } from '../redux/reducer';

describe('rolesReducer', () => {
  it('lưu danh sách vai trò', () => {
    const items = [{ id: 'r1', name: 'A' }];
    const state = rolesReducer(undefined, { type: ROLES_LIST_SUCCESS, payload: items });
    expect(state.items).toEqual(items);
  });

  it('thêm vai trò mới khi upsert chưa tồn tại', () => {
    const state = rolesReducer({ items: [], loading: false, error: null, selectedId: null }, {
      type: ROLES_UPSERT, payload: { id: 'r2', name: 'B' },
    });
    expect(state.items).toHaveLength(1);
    expect(state.selectedId).toBe('r2');
  });

  it('cập nhật vai trò đã tồn tại khi upsert', () => {
    const initial = { items: [{ id: 'r1', name: 'A', permissions: [] }], loading: false, error: null, selectedId: null };
    const state = rolesReducer(initial, { type: ROLES_UPSERT, payload: { id: 'r1', name: 'A', permissions: ['x'] } });
    expect(state.items[0].permissions).toEqual(['x']);
  });

  it('chọn vai trò', () => {
    const state = rolesReducer(undefined, { type: ROLES_SELECT, payload: 'r1' });
    expect(state.selectedId).toBe('r1');
  });
});

describe('rolesReducer — selectedId khi upsert', () => {
  const base = { items: [{ id: 'a', name: 'A' }, { id: 'b', name: 'B' }], loading: false, error: null, selectedId: 'b' };

  it('cập nhật role đã có không đổi selectedId', () => {
    const state = rolesReducer(base, { type: ROLES_UPSERT, payload: { id: 'a', name: 'A2' } });
    expect(state.selectedId).toBe('b');
  });

  it('tạo mới thì chọn role vừa tạo', () => {
    const state = rolesReducer(base, { type: ROLES_UPSERT, payload: { id: 'c', name: 'C' } });
    expect(state.selectedId).toBe('c');
  });
});

import adminReducer, {
  ADMIN_USERS_LIST_REQUEST, ADMIN_USERS_LIST_SUCCESS, ADMIN_USERS_SELECT, ADMIN_USER_UPDATED,
} from '../redux/reducer';

describe('adminReducer', () => {
  it('bật loading khi bắt đầu tải danh sách', () => {
    const state = adminReducer(undefined, { type: ADMIN_USERS_LIST_REQUEST });
    expect(state.loading).toBe(true);
  });

  it('lưu danh sách và phân trang khi tải xong', () => {
    const payload = {
      items: [{ id: 'u1' }], pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1,
    };
    const state = adminReducer(undefined, { type: ADMIN_USERS_LIST_SUCCESS, payload });
    expect(state.items).toEqual(payload.items);
    expect(state.loading).toBe(false);
  });

  it('cập nhật một user trong danh sách khi có ADMIN_USER_UPDATED', () => {
    const initial = { items: [{ id: 'u1', status: 'active' }], loading: false, error: null, selectedId: null, pageNumber: 1, pageSize: 10, totalCount: 1, totalPages: 1 };
    const state = adminReducer(initial, { type: ADMIN_USER_UPDATED, payload: { id: 'u1', status: 'locked' } });
    expect(state.items[0].status).toBe('locked');
  });

  it('chọn user hiện tại', () => {
    const state = adminReducer(undefined, { type: ADMIN_USERS_SELECT, payload: 'u1' });
    expect(state.selectedId).toBe('u1');
  });
});

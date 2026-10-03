import adminReducer, {
  ADMIN_USERS_LIST_REQUEST, ADMIN_USERS_LIST_SUCCESS, ADMIN_USERS_SELECT, ADMIN_USER_UPDATED,
  ADMIN_USER_DETAIL_SUCCESS, ADMIN_USER_DETAIL_FAILURE,
} from '../redux/reducer';

const summary = (id, extra = {}) => ({
  id, email: `${id}@x.vn`, fullName: id, isActive: true, mustChangePassword: false, roles: [], rowVersion: 1, ...extra,
});

describe('adminReducer', () => {
  it('bật loading khi bắt đầu tải danh sách', () => {
    expect(adminReducer(undefined, { type: ADMIN_USERS_LIST_REQUEST }).loading).toBe(true);
  });

  it('lưu danh sách và phân trang khi tải xong', () => {
    const payload = {
      items: [summary('u1')], pageNumber: 2, pageSize: 10, totalCount: 11, totalPages: 2,
    };
    const state = adminReducer(undefined, { type: ADMIN_USERS_LIST_SUCCESS, payload });
    expect(state.items).toEqual(payload.items);
    expect(state.pageNumber).toBe(2);
    expect(state.loading).toBe(false);
  });

  it('chọn user xóa chi tiết cũ và chờ tải chi tiết', () => {
    const initial = { ...adminReducer(undefined, {}), detail: { id: 'u0' } };
    const state = adminReducer(initial, { type: ADMIN_USERS_SELECT, payload: 'u1' });
    expect(state.selectedId).toBe('u1');
    expect(state.detail).toBeNull();
    expect(state.detailLoading).toBe(true);
  });

  it('chi tiết của user không còn được chọn bị bỏ', () => {
    const initial = { ...adminReducer(undefined, {}), selectedId: 'u2' };
    const stale = adminReducer(initial, { type: ADMIN_USER_DETAIL_SUCCESS, payload: { ...summary('u1'), permissionGrants: [] } });
    expect(stale.detail).toBeNull();
    const failed = adminReducer(initial, { type: ADMIN_USER_DETAIL_FAILURE, payload: { id: 'u1', message: 'x' } });
    expect(failed.detailError).toBeNull();
  });

  it('ADMIN_USER_UPDATED chỉ cập nhật đúng Id (danh sách và chi tiết)', () => {
    const initial = {
      ...adminReducer(undefined, {}),
      items: [summary('u1'), summary('u2')],
      selectedId: 'u2',
      detail: { ...summary('u2'), permissionGrants: [], effectivePermissions: [] },
    };
    const forU1 = adminReducer(initial, { type: ADMIN_USER_UPDATED, payload: { ...summary('u1', { rowVersion: 9 }), permissionGrants: [] } });
    expect(forU1.items[0].rowVersion).toBe(9);
    expect(forU1.items[1].rowVersion).toBe(1);
    expect(forU1.detail.id).toBe('u2');
    expect(forU1.detail.rowVersion).toBe(1);
    const forU2 = adminReducer(initial, { type: ADMIN_USER_UPDATED, payload: { ...summary('u2', { rowVersion: 4 }), permissionGrants: [] } });
    expect(forU2.detail.rowVersion).toBe(4);
  });
});

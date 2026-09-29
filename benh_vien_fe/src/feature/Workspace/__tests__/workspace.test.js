import workspaceReducer, { WORKSPACE_SELECTED, WORKSPACE_CLEARED } from '../redux/reducer';
import { selectWorkspace, clearWorkspace } from '../redux/action';
import { workspacesForUser } from '../workspaces';
import { PERMISSIONS } from '../../Auth/permissionCodes';

describe('workspaceReducer', () => {
  it('lưu khu vực đã chọn', () => {
    const state = workspaceReducer(undefined, selectWorkspace('reception'));
    expect(state).toEqual({ selectedId: 'reception' });
    expect(selectWorkspace('reception').type).toBe(WORKSPACE_SELECTED);
  });

  it('xóa khu vực đã chọn', () => {
    const state = workspaceReducer({ selectedId: 'reception' }, clearWorkspace());
    expect(state).toEqual({ selectedId: null });
    expect(clearWorkspace().type).toBe(WORKSPACE_CLEARED);
  });
});

describe('workspacesForUser', () => {
  it('trả về rỗng khi không có quyền nào khớp', () => {
    expect(workspacesForUser([])).toEqual([]);
  });

  it('chỉ trả về khu vực mà người dùng có ít nhất một quyền cần', () => {
    const result = workspacesForUser([PERMISSIONS.PATIENTS_READ]);
    expect(result).toHaveLength(1);
    expect(result[0].id).toBe('reception');
  });

  it('trả về nhiều khu vực khi có nhiều quyền', () => {
    const result = workspacesForUser([PERMISSIONS.USERS_READ, PERMISSIONS.VITALS_RECORD]);
    expect(result.map(w => w.id).sort()).toEqual(['admin', 'vitals']);
  });
});

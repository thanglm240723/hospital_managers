import { createStore, applyMiddleware } from 'redux';
import thunk from 'redux-thunk';
import reducer from 'reducer';
import { login } from 'feature/Auth/redux/actions';
import * as authClient from 'feature/Auth/api/authClient';
import http from 'service/http';
import { PERMISSIONS } from 'feature/Auth/permissionCodes';
import workspaceReducer, { WORKSPACE_SELECTED, WORKSPACE_CLEARED } from '../redux/reducer';
import { selectWorkspace, clearWorkspace } from '../redux/action';
import {
  getAvailableWorkspaces, getDefaultRoute, canAccessRoute, routeStatus, resolveStartTarget, getMenuRoutes,
} from '../routeAccess';
import { FEATURE_AVAILABILITY } from '../availability';

jest.mock('feature/Auth/api/authClient', () => ({ refresh: jest.fn(), login: jest.fn(), logout: jest.fn() }));
jest.mock('service/http', () => ({ get: jest.fn(), post: jest.fn() }));

const ON = {
  'admin.users': true,
  'admin.roles': true,
  'reception.patients': true,
  'reception.intake': true,
  'clinic.queue': true,
  'clinic.encounter': true,
  'vitals.queue': true,
};

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

describe('route registry', () => {
  it('không có quyền → không khu vực', () => {
    expect(getAvailableWorkspaces([], ON)).toEqual([]);
  });

  it('khu vực theo quyền, không theo availability', () => {
    const ids = getAvailableWorkspaces([PERMISSIONS.ROLES_READ, PERMISSIONS.VITALS_RECORD], {}).map(w => w.id);
    expect(ids).toEqual(['admin', 'vitals']);
    expect(getAvailableWorkspaces([PERMISSIONS.ROLES_READ], {})[0].ready).toBe(false);
    expect(getAvailableWorkspaces([PERMISSIONS.ROLES_READ], ON)[0].ready).toBe(true);
  });

  it('default route theo quyền: chỉ roles.read → /admin/roles; có users.read → /admin/users', () => {
    expect(getDefaultRoute('admin', [PERMISSIONS.ROLES_READ], ON)).toBe('/admin/roles');
    expect(getDefaultRoute('admin', [PERMISSIONS.ROLES_READ, PERMISSIONS.USERS_READ], ON)).toBe('/admin/users');
    expect(getDefaultRoute('admin', [PERMISSIONS.VITALS_RECORD], ON)).toBeNull();
  });

  it('default route ưu tiên màn đã phát hành', () => {
    expect(getDefaultRoute('admin', [PERMISSIONS.ROLES_READ, PERMISSIONS.USERS_READ], { 'admin.roles': true })).toBe('/admin/roles');
  });

  it('canAccessRoute cần cả quyền và availability; routeStatus phân biệt', () => {
    expect(canAccessRoute('/admin/roles', [PERMISSIONS.ROLES_READ], ON)).toBe(true);
    expect(canAccessRoute('/admin/roles', [PERMISSIONS.ROLES_READ], {})).toBe(false);
    expect(routeStatus('/admin/roles', [PERMISSIONS.ROLES_READ], {})).toBe('unavailable');
    expect(routeStatus('/admin/users', [PERMISSIONS.ROLES_READ], ON)).toBe('forbidden');
    expect(routeStatus('/clinic/encounters/abc', [PERMISSIONS.ENCOUNTERS_EXAMINE], ON)).toBe('ok');
  });

  it('menu chỉ của khu vực được truyền', () => {
    const all = [PERMISSIONS.USERS_READ, PERMISSIONS.ROLES_READ, PERMISSIONS.VITALS_RECORD];
    expect(getMenuRoutes('vitals', all, ON).map(r => r.path)).toEqual(['/vitals']);
  });

  it('return URL: chỉ route nội bộ, cùng khu vực, có quyền', () => {
    const perms = [PERMISSIONS.PATIENTS_READ, PERMISSIONS.VITALS_RECORD];
    expect(resolveStartTarget('reception', { pathname: '/reception/intake/p1' }, perms, ON)).toEqual({ pathname: '/reception/intake/p1', search: '' });
    expect(resolveStartTarget('reception', { pathname: '/vitals' }, perms, ON)).toBe('/reception/patients');
    expect(resolveStartTarget('reception', { pathname: '//evil.example' }, perms, ON)).toBe('/reception/patients');
    expect(resolveStartTarget('admin', { pathname: '/admin/users' }, [PERMISSIONS.ROLES_READ], ON)).toBe('/admin/roles');
  });

  it('mốc nền lớp 2: chỉ admin.users, admin.roles, admin.facilities đã phát hành, các màn còn lại chưa (không giả quyền/mock)', () => {
    const on = Object.keys(FEATURE_AVAILABILITY).filter(k => FEATURE_AVAILABILITY[k]);
    expect(on).toEqual(['admin.users', 'admin.roles', 'admin.facilities']);
  });
});

describe('login clear khu vực', () => {
  const meData = { id: 'u1', permissions: [PERMISSIONS.USERS_READ, PERMISSIONS.VITALS_RECORD], mustChangePassword: false };
  beforeEach(() => {
    jest.clearAllMocks();
    authClient.login.mockResolvedValue({ accessToken: 't', expiresAtUtc: new Date(Date.now() + 600000).toISOString() });
    http.get.mockResolvedValue({ data: meData });
  });

  it('mỗi lần nhập credential xoá selectedId cũ kể cả cùng user; nhiều khu vực phải chọn lại', async () => {
    const store = createStore(reducer, applyMiddleware(thunk));
    await store.dispatch(login('u1@benhvien.vn', 'x'));
    store.dispatch(selectWorkspace('vitals'));
    expect(store.getState().workspace.selectedId).toBe('vitals');

    await store.dispatch(login('u1@benhvien.vn', 'x'));
    expect(store.getState().workspace.selectedId).toBeNull();
    expect(store.getState().auth.status).toBe('authenticated');
  });

  it('login thất bại cũng không giữ lựa chọn cũ', async () => {
    const store = createStore(reducer, applyMiddleware(thunk));
    store.dispatch(selectWorkspace('vitals'));
    authClient.login.mockRejectedValue(new Error('401'));
    await expect(store.dispatch(login('a', 'b'))).rejects.toThrow('401');
    expect(store.getState().workspace.selectedId).toBeNull();
  });
});

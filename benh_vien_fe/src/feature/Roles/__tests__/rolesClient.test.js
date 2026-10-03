import http from 'service/http';
import {
  listRoles, listPermissions, createRole, renameRole, updateRolePermissions,
} from '../api/rolesClient';

jest.mock('service/http', () => ({
  get: jest.fn(), post: jest.fn(), put: jest.fn(),
}));

describe('rolesClient', () => {
  beforeEach(() => {
    http.get.mockReset().mockResolvedValue({ data: [] });
    http.post.mockReset().mockResolvedValue({ data: { id: 'r' } });
    http.put.mockReset().mockResolvedValue({ data: { id: 'r', rowVersion: 8 } });
  });

  it('listRoles/listPermissions gọi GET đúng route', async () => {
    await listRoles();
    await listPermissions();
    expect(http.get).toHaveBeenNthCalledWith(1, 'v1/roles');
    expect(http.get).toHaveBeenNthCalledWith(2, 'v1/permissions');
  });

  it('createRole chỉ gửi code, name, permissionCodes (không gửi isSystem)', async () => {
    await createRole({
      code: 'x', name: 'X', permissionCodes: ['a'], isSystem: true,
    });
    expect(http.post).toHaveBeenCalledWith('v1/roles', { code: 'x', name: 'X', permissionCodes: ['a'] });
  });

  it('renameRole dùng PUT + If-Match', async () => {
    await renameRole('id1', 'Tên', 7);
    expect(http.put).toHaveBeenCalledWith('v1/roles/id1', { name: 'Tên' }, { headers: { 'If-Match': '7' } });
  });

  it('updateRolePermissions dùng PUT /permissions + If-Match', async () => {
    const result = await updateRolePermissions('id1', ['a', 'b'], 7);
    expect(http.put).toHaveBeenCalledWith('v1/roles/id1/permissions', { permissionCodes: ['a', 'b'] }, { headers: { 'If-Match': '7' } });
    expect(result.rowVersion).toBe(8);
  });
});

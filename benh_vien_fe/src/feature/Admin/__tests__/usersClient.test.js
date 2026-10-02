import http from 'service/http';
import {
  listUsers, getUser, createUser, assignRoles, grantPermission, revokePermission, activateUser, deactivateUser,
} from '../api/usersClient';

jest.mock('service/http', () => ({
  get: jest.fn(), post: jest.fn(), put: jest.fn(),
}));

describe('usersClient', () => {
  beforeEach(() => {
    http.get.mockReset().mockResolvedValue({ data: {} });
    http.post.mockReset().mockResolvedValue({ data: { id: 'u' } });
    http.put.mockReset().mockResolvedValue({ data: { id: 'u', rowVersion: 8 } });
  });

  it('listUsers gửi phân trang/lọc, bỏ tham số rỗng', async () => {
    await listUsers({ pageNumber: 2, pageSize: 10, searchTerm: 'an', roleId: '', status: 'locked' });
    expect(http.get).toHaveBeenCalledWith('v1/users', { params: { pageNumber: 2, pageSize: 10, searchTerm: 'an', status: 'locked' } });
  });

  it('getUser gọi GET v1/users/{id}', async () => {
    await getUser('u1');
    expect(http.get).toHaveBeenCalledWith('v1/users/u1');
  });

  it('createUser chỉ gửi email, fullName, roleIds (không gửi mật khẩu)', async () => {
    await createUser({ email: 'a@b.vn', fullName: 'A', roleIds: ['r1'], password: 'x' });
    expect(http.post).toHaveBeenCalledWith('v1/users', { email: 'a@b.vn', fullName: 'A', roleIds: ['r1'] });
  });

  it('assignRoles dùng PUT + If-Match = rowVersion', async () => {
    await assignRoles('u1', ['r1'], 5);
    expect(http.put).toHaveBeenCalledWith('v1/users/u1/roles', { roleIds: ['r1'] }, { headers: { 'If-Match': '5' } });
  });

  it('grant/revoke gửi một permissionCode + reason và If-Match', async () => {
    await grantPermission('u1', 'a.read', 'Lý do', 6);
    await revokePermission('u1', 'a.read', 'Lý do 2', 7);
    expect(http.post).toHaveBeenNthCalledWith(1, 'v1/users/u1/permissions/grant', { permissionCode: 'a.read', reason: 'Lý do' }, { headers: { 'If-Match': '6' } });
    expect(http.post).toHaveBeenNthCalledWith(2, 'v1/users/u1/permissions/revoke', { permissionCode: 'a.read', reason: 'Lý do 2' }, { headers: { 'If-Match': '7' } });
  });

  it('activate/deactivate không gửi If-Match', async () => {
    await activateUser('u1');
    await deactivateUser('u1');
    expect(http.post).toHaveBeenNthCalledWith(1, 'v1/users/u1/activate');
    expect(http.post).toHaveBeenNthCalledWith(2, 'v1/users/u1/deactivate');
  });
});

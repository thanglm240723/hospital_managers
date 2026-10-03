import http from 'service/http';
import {
  getFacilityTree, createBranch, createDepartment, createRoom, updateFacility,
} from '../api/facilitiesClient';

jest.mock('service/http', () => ({ get: jest.fn(), post: jest.fn(), put: jest.fn() }));

describe('facilitiesClient', () => {
  beforeEach(() => {
    http.get.mockReset().mockResolvedValue({ data: [] });
    http.post.mockReset().mockResolvedValue({ data: { id: 'x' } });
    http.put.mockReset().mockResolvedValue({ data: { id: 'x', rowVersion: 9 } });
  });

  it('getFacilityTree gọi GET v1/facilities', async () => {
    await getFacilityTree();
    expect(http.get).toHaveBeenCalledWith('v1/facilities');
  });

  it('create* gọi POST đúng route và body', async () => {
    await createBranch({ code: 'CS1', name: 'Cơ sở 1', extra: 1 });
    await createDepartment({
      branchId: 'b', code: 'K1', name: 'Khoa', kind: 'clinical',
    });
    await createRoom({ departmentId: 'd', code: 'P1', name: 'Phòng' });
    expect(http.post).toHaveBeenNthCalledWith(1, 'v1/facilities/branches', { code: 'CS1', name: 'Cơ sở 1' });
    expect(http.post).toHaveBeenNthCalledWith(2, 'v1/facilities/departments', {
      branchId: 'b', code: 'K1', name: 'Khoa', kind: 'clinical',
    });
    expect(http.post).toHaveBeenNthCalledWith(3, 'v1/facilities/rooms', { departmentId: 'd', code: 'P1', name: 'Phòng' });
  });

  it('updateFacility dùng PUT + If-Match', async () => {
    const result = await updateFacility('rooms', 'r1', { name: 'N', isActive: false, code: 'bỏ' }, 7);
    expect(http.put).toHaveBeenCalledWith('v1/facilities/rooms/r1', { name: 'N', isActive: false }, { headers: { 'If-Match': '7' } });
    expect(result.rowVersion).toBe(9);
  });
});

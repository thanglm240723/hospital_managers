import http from 'service/http';
import {
  getStaffProfile, createStaffProfile, updateStaffProfile, setWorkScopes,
} from '../api/staffProfileClient';

jest.mock('service/http', () => ({ get: jest.fn(), put: jest.fn() }));

describe('staffProfileClient', () => {
  beforeEach(() => {
    http.get.mockReset().mockResolvedValue({ data: { id: 'p' } });
    http.put.mockReset().mockResolvedValue({ data: { id: 'p', rowVersion: 2 } });
  });

  it('getStaffProfile GET đúng URL', async () => {
    expect(await getStaffProfile('u1')).toEqual({ id: 'p' });
    expect(http.get).toHaveBeenCalledWith('v1/users/u1/staff-profile');
  });

  it('createStaffProfile PUT không If-Match', async () => {
    await createStaffProfile('u1', { staffCode: 'NV1', isActive: true });
    expect(http.put).toHaveBeenCalledWith('v1/users/u1/staff-profile', { staffCode: 'NV1', isActive: true });
  });

  it('updateStaffProfile PUT kèm If-Match', async () => {
    await updateStaffProfile('u1', { staffCode: 'NV1', isActive: false }, 4);
    expect(http.put).toHaveBeenCalledWith('v1/users/u1/staff-profile', { staffCode: 'NV1', isActive: false }, { headers: { 'If-Match': '4' } });
  });

  it('setWorkScopes PUT work-scopes kèm If-Match', async () => {
    await setWorkScopes('u1', ['d1', 'd2'], 7);
    expect(http.put).toHaveBeenCalledWith('v1/users/u1/staff-profile/work-scopes', { departmentIds: ['d1', 'd2'] }, { headers: { 'If-Match': '7' } });
  });
});

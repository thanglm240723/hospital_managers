import reducer, { FACILITIES_LOAD_REQUEST, FACILITIES_LOAD_SUCCESS, FACILITIES_LOAD_FAILURE } from '../redux/reducer';

describe('facilities reducer', () => {
  it('REQUEST bật loading và xóa lỗi', () => {
    const s = reducer({ branches: [], loading: false, error: 'x' }, { type: FACILITIES_LOAD_REQUEST });
    expect(s).toEqual({ branches: [], loading: true, error: null });
  });

  it('SUCCESS lưu cây', () => {
    const s = reducer(undefined, { type: FACILITIES_LOAD_SUCCESS, payload: [{ id: 'b' }] });
    expect(s.branches).toEqual([{ id: 'b' }]);
    expect(s.loading).toBe(false);
  });

  it('FAILURE lưu lỗi, giữ cây cũ', () => {
    const s = reducer({ branches: [{ id: 'b' }], loading: true, error: null }, { type: FACILITIES_LOAD_FAILURE, payload: 'lỗi' });
    expect(s).toEqual({ branches: [{ id: 'b' }], loading: false, error: 'lỗi' });
  });
});

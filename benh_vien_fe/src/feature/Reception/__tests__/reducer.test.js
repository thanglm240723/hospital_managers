import receptionReducer, {
  RECEPTION_SEARCH_RESULTS, RECEPTION_VISIT_REGISTERED, RECEPTION_VISIT_CLEARED,
} from '../redux/reducer';

describe('receptionReducer', () => {
  it('lưu kết quả tìm kiếm', () => {
    const items = [{ id: 'p1' }];
    const state = receptionReducer(undefined, { type: RECEPTION_SEARCH_RESULTS, payload: items });
    expect(state.results).toEqual(items);
  });

  it('lưu lượt khám vừa cấp số', () => {
    const visit = { id: 'v1', queueNumber: 12 };
    const state = receptionReducer(undefined, { type: RECEPTION_VISIT_REGISTERED, payload: visit });
    expect(state.lastVisit).toEqual(visit);
  });

  it('xóa lượt khám khi rời màn', () => {
    const state = receptionReducer({ results: [], lastVisit: { id: 'v1' } }, { type: RECEPTION_VISIT_CLEARED });
    expect(state.lastVisit).toBeNull();
  });
});

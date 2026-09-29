import vitalsReducer, {
  VITALS_QUEUE_SUCCESS, VITALS_CALL_NEXT, VITALS_COMPLETED, VITALS_CLEAR,
} from '../redux/reducer';

const queue = [{ id: 'a' }, { id: 'b' }];

describe('vitalsReducer', () => {
  it('lưu hàng chờ', () => {
    const state = vitalsReducer(undefined, { type: VITALS_QUEUE_SUCCESS, payload: queue });
    expect(state.queue).toEqual(queue);
  });

  it('gọi lượt tiếp theo lấy người đầu hàng', () => {
    const loaded = vitalsReducer(undefined, { type: VITALS_QUEUE_SUCCESS, payload: queue });
    const state = vitalsReducer(loaded, { type: VITALS_CALL_NEXT });
    expect(state.current).toEqual({ id: 'a' });
    expect(state.queue).toEqual([{ id: 'b' }]);
  });

  it('không đổi state khi hàng trống', () => {
    const state = vitalsReducer(undefined, { type: VITALS_CALL_NEXT });
    expect(state.current).toBeNull();
  });

  it('nạp lại hàng chờ không đưa người đang đo trở lại hàng', () => {
    const calling = vitalsReducer(vitalsReducer(undefined, { type: VITALS_QUEUE_SUCCESS, payload: queue }), { type: VITALS_CALL_NEXT });
    const state = vitalsReducer(calling, { type: VITALS_QUEUE_SUCCESS, payload: queue });
    expect(state.queue).toEqual([{ id: 'b' }]);
  });

  it('hoàn thành bỏ người đang đo, clear xóa sạch', () => {
    const calling = vitalsReducer(vitalsReducer(undefined, { type: VITALS_QUEUE_SUCCESS, payload: queue }), { type: VITALS_CALL_NEXT });
    expect(vitalsReducer(calling, { type: VITALS_COMPLETED }).current).toBeNull();
    expect(vitalsReducer(calling, { type: VITALS_CLEAR }).queue).toEqual([]);
  });
});

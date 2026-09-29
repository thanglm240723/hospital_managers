export const VITALS_QUEUE_REQUEST = 'HMS/VITALS/QUEUE_REQUEST';
export const VITALS_QUEUE_SUCCESS = 'HMS/VITALS/QUEUE_SUCCESS';
export const VITALS_QUEUE_FAILURE = 'HMS/VITALS/QUEUE_FAILURE';
export const VITALS_CALL_NEXT = 'HMS/VITALS/CALL_NEXT';
export const VITALS_COMPLETED = 'HMS/VITALS/COMPLETED';
export const VITALS_CLEAR = 'HMS/VITALS/CLEAR';

const initialState = {
  queue: [], current: null, loading: false, error: null,
};

export default function vitalsReducer(state = initialState, action) {
  switch (action.type) {
    case VITALS_QUEUE_REQUEST:
      return { ...state, loading: true, error: null };
    case VITALS_QUEUE_SUCCESS: {
      // Người đang đo không hiện lại trong hàng chờ.
      const currentId = state.current && state.current.id;
      return { ...state, loading: false, queue: action.payload.filter(item => item.id !== currentId) };
    }
    case VITALS_QUEUE_FAILURE:
      return { ...state, loading: false, error: action.payload };
    case VITALS_CALL_NEXT: {
      const [next, ...rest] = state.queue;
      return next ? { ...state, current: next, queue: rest } : state;
    }
    case VITALS_COMPLETED:
      return { ...state, current: null };
    case VITALS_CLEAR:
      return initialState;
    default:
      return state;
  }
}

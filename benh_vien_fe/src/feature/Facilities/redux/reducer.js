export const FACILITIES_LOAD_REQUEST = 'HMS/FACILITIES/LOAD_REQUEST';
export const FACILITIES_LOAD_SUCCESS = 'HMS/FACILITIES/LOAD_SUCCESS';
export const FACILITIES_LOAD_FAILURE = 'HMS/FACILITIES/LOAD_FAILURE';

const initialState = { branches: [], loading: false, error: null };

export default function facilitiesReducer(state = initialState, action) {
  switch (action.type) {
    case FACILITIES_LOAD_REQUEST:
      return { ...state, loading: true, error: null };
    case FACILITIES_LOAD_SUCCESS:
      return { ...state, loading: false, branches: action.payload };
    case FACILITIES_LOAD_FAILURE:
      return { ...state, loading: false, error: action.payload };
    default:
      return state;
  }
}

export const RECEPTION_SEARCH_RESULTS = 'HMS/RECEPTION/SEARCH_RESULTS';
export const RECEPTION_VISIT_REGISTERED = 'HMS/RECEPTION/VISIT_REGISTERED';
export const RECEPTION_VISIT_CLEARED = 'HMS/RECEPTION/VISIT_CLEARED';

const initialState = { results: [], lastVisit: null };

export default function receptionReducer(state = initialState, action) {
  switch (action.type) {
    case RECEPTION_SEARCH_RESULTS:
      return { ...state, results: action.payload };
    case RECEPTION_VISIT_REGISTERED:
      return { ...state, lastVisit: action.payload };
    case RECEPTION_VISIT_CLEARED:
      return { ...state, lastVisit: null };
    default:
      return state;
  }
}

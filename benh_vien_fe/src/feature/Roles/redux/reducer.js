export const ROLES_LIST_REQUEST = 'HMS/ROLES/LIST_REQUEST';
export const ROLES_LIST_SUCCESS = 'HMS/ROLES/LIST_SUCCESS';
export const ROLES_LIST_FAILURE = 'HMS/ROLES/LIST_FAILURE';
export const ROLES_SELECT = 'HMS/ROLES/SELECT';
export const ROLES_UPSERT = 'HMS/ROLES/UPSERT';

const initialState = {
  items: [], loading: false, error: null, selectedId: null,
};

export default function rolesReducer(state = initialState, action) {
  switch (action.type) {
    case ROLES_LIST_REQUEST:
      return { ...state, loading: true, error: null };
    case ROLES_LIST_SUCCESS:
      return { ...state, loading: false, items: action.payload };
    case ROLES_LIST_FAILURE:
      return { ...state, loading: false, error: action.payload };
    case ROLES_SELECT:
      return { ...state, selectedId: action.payload };
    case ROLES_UPSERT: {
      const exists = state.items.some(r => r.id === action.payload.id);
      return {
        ...state,
        items: exists ? state.items.map(r => (r.id === action.payload.id ? action.payload : r)) : [...state.items, action.payload],
        selectedId: action.payload.id,
      };
    }
    default:
      return state;
  }
}

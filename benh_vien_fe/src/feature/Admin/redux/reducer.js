export const ADMIN_USERS_LIST_REQUEST = 'HMS/ADMIN/USERS_LIST_REQUEST';
export const ADMIN_USERS_LIST_SUCCESS = 'HMS/ADMIN/USERS_LIST_SUCCESS';
export const ADMIN_USERS_LIST_FAILURE = 'HMS/ADMIN/USERS_LIST_FAILURE';
export const ADMIN_USERS_SELECT = 'HMS/ADMIN/USERS_SELECT';
export const ADMIN_USER_UPDATED = 'HMS/ADMIN/USER_UPDATED';

const initialState = {
  items: [],
  pageNumber: 1,
  pageSize: 10,
  totalCount: 0,
  totalPages: 1,
  loading: false,
  error: null,
  selectedId: null,
};

export default function adminReducer(state = initialState, action) {
  switch (action.type) {
    case ADMIN_USERS_LIST_REQUEST:
      return { ...state, loading: true, error: null };
    case ADMIN_USERS_LIST_SUCCESS:
      return {
        ...state,
        loading: false,
        items: action.payload.items,
        pageNumber: action.payload.pageNumber,
        pageSize: action.payload.pageSize,
        totalCount: action.payload.totalCount,
        totalPages: action.payload.totalPages,
      };
    case ADMIN_USERS_LIST_FAILURE:
      return { ...state, loading: false, error: action.payload };
    case ADMIN_USERS_SELECT:
      return { ...state, selectedId: action.payload };
    case ADMIN_USER_UPDATED:
      return {
        ...state,
        items: state.items.map(u => (u.id === action.payload.id ? action.payload : u)),
      };
    default:
      return state;
  }
}

export const ADMIN_USERS_LIST_REQUEST = 'HMS/ADMIN/USERS_LIST_REQUEST';
export const ADMIN_USERS_LIST_SUCCESS = 'HMS/ADMIN/USERS_LIST_SUCCESS';
export const ADMIN_USERS_LIST_FAILURE = 'HMS/ADMIN/USERS_LIST_FAILURE';
export const ADMIN_USERS_SELECT = 'HMS/ADMIN/USERS_SELECT';
export const ADMIN_USER_DETAIL_REQUEST = 'HMS/ADMIN/USER_DETAIL_REQUEST';
export const ADMIN_USER_DETAIL_SUCCESS = 'HMS/ADMIN/USER_DETAIL_SUCCESS';
export const ADMIN_USER_DETAIL_FAILURE = 'HMS/ADMIN/USER_DETAIL_FAILURE';
export const ADMIN_USER_UPDATED = 'HMS/ADMIN/USER_UPDATED';
export const ADMIN_ROLE_OPTIONS_SUCCESS = 'HMS/ADMIN/ROLE_OPTIONS_SUCCESS';
export const ADMIN_ROLE_OPTIONS_FAILURE = 'HMS/ADMIN/ROLE_OPTIONS_FAILURE';
export const ADMIN_PERMISSIONS_SUCCESS = 'HMS/ADMIN/PERMISSIONS_SUCCESS';
export const ADMIN_PERMISSIONS_FAILURE = 'HMS/ADMIN/PERMISSIONS_FAILURE';

const initialState = {
  items: [],
  pageNumber: 1,
  pageSize: 10,
  totalCount: 0,
  totalPages: 1,
  loading: false,
  error: null,
  selectedId: null,
  detail: null,
  detailLoading: false,
  detailError: null,
  roleOptions: [],
  roleOptionsError: null,
  permissions: [],
  permissionsError: null,
};

const SUMMARY_FIELDS = ['email', 'fullName', 'isActive', 'mustChangePassword', 'roles', 'rowVersion'];
const toSummary = dto => SUMMARY_FIELDS.reduce((acc, key) => ({ ...acc, [key]: dto[key] }), { id: dto.id });

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
      return {
        ...state, selectedId: action.payload, detail: null, detailLoading: Boolean(action.payload), detailError: null,
      };
    case ADMIN_USER_DETAIL_REQUEST:
      return action.payload === state.selectedId ? { ...state, detailLoading: true, detailError: null } : state;
    // Chỉ nhận chi tiết của đúng user đang chọn: phản hồi trễ của user trước bị bỏ.
    case ADMIN_USER_DETAIL_SUCCESS:
      return action.payload.id === state.selectedId
        ? {
          ...state,
          detail: action.payload,
          detailLoading: false,
          detailError: null,
          items: state.items.map(u => (u.id === action.payload.id ? toSummary(action.payload) : u)),
        }
        : state;
    case ADMIN_USER_DETAIL_FAILURE:
      return action.payload.id === state.selectedId ? { ...state, detailLoading: false, detailError: action.payload.message } : state;
    // Kết quả command: chỉ cập nhật đúng thực thể có cùng Id (danh sách và chi tiết).
    case ADMIN_USER_UPDATED:
      return {
        ...state,
        items: state.items.map(u => (u.id === action.payload.id ? toSummary(action.payload) : u)),
        detail: state.detail && state.detail.id === action.payload.id ? action.payload : state.detail,
      };
    case ADMIN_ROLE_OPTIONS_SUCCESS:
      return { ...state, roleOptions: action.payload, roleOptionsError: null };
    case ADMIN_ROLE_OPTIONS_FAILURE:
      return { ...state, roleOptions: [], roleOptionsError: action.payload };
    case ADMIN_PERMISSIONS_SUCCESS:
      return { ...state, permissions: action.payload, permissionsError: null };
    case ADMIN_PERMISSIONS_FAILURE:
      return { ...state, permissions: [], permissionsError: action.payload };
    default:
      return state;
  }
}

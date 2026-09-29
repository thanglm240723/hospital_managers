import {
  AUTH_BOOTING, AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_LOGGED_OUT,
} from './actionTypes';

// Redux chỉ giữ thông tin hiển thị. Token KHÔNG nằm ở đây (xem session/tokenStore.js).
const initialState = {
  status: 'booting',
  user: null,
  permissions: [],
  mustChangePassword: false,
  sessionMessage: null,
};

export default function authReducer(state = initialState, action) {
  switch (action.type) {
    case AUTH_BOOTING:
      return { ...initialState };
    case AUTH_AUTHENTICATED: {
      const { permissions, mustChangePassword, ...user } = action.payload;
      return {
        status: 'authenticated', user, permissions, mustChangePassword, sessionMessage: null,
      };
    }
    case AUTH_ANONYMOUS:
    case AUTH_LOGGED_OUT:
      return { ...initialState, status: 'anonymous', sessionMessage: action.payload || null };
    default:
      return state;
  }
}

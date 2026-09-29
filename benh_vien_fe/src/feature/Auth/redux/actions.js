import http from '../../../service/http';
import * as authClient from '../api/authClient';
import { refreshAccessToken, broadcastLogout } from '../session/refreshCoordinator';
import { setAccessToken, clearAccessToken } from '../session/tokenStore';
import {
  AUTH_BOOTING, AUTH_AUTHENTICATED, AUTH_ANONYMOUS, AUTH_LOGGED_OUT,
} from './actionTypes';

export const SESSION_EXPIRED_MESSAGE = 'Phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại.';

export const loadMe = () => async (dispatch) => {
  const { data } = await http.get('v1/auth/me');
  dispatch({ type: AUTH_AUTHENTICATED, payload: data });
  return data;
};

// Khởi động/F5: không có token trong RAM ⇒ thử refresh bằng cookie.
export const bootAuth = () => async (dispatch) => {
  dispatch({ type: AUTH_BOOTING });
  try {
    await refreshAccessToken();
    await dispatch(loadMe());
  } catch (error) {
    clearAccessToken();
    dispatch({ type: AUTH_ANONYMOUS });
  }
};

export const login = (email, password) => async (dispatch) => {
  const data = await authClient.login(email, password);
  setAccessToken(data.accessToken, data.expiresAtUtc);
  return dispatch(loadMe());
};

function endLocalSession(dispatch, message) {
  clearAccessToken();
  dispatch({ type: AUTH_LOGGED_OUT, payload: message || null });
}

export const logout = message => async (dispatch) => {
  try {
    await authClient.logout();
  } catch (error) {
    // Vẫn xoá phiên cục bộ dù server không phản hồi.
  }
  endLocalSession(dispatch, message);
  broadcastLogout();
};

export const expireSession = () => (dispatch) => {
  endLocalSession(dispatch, SESSION_EXPIRED_MESSAGE);
  broadcastLogout();
};

export const changePassword = (currentPassword, newPassword) => async (dispatch) => {
  const { data } = await http.post('v1/auth/change-password', { currentPassword, newPassword });
  setAccessToken(data.accessToken, data.expiresAtUtc);
  return dispatch(loadMe());
};
